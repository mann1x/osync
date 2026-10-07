using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Born2Code.Net;

namespace osync
{
    /// <summary>
    /// Temporary, minimal OCI-registry endpoint used to copy a model out of an Ollama server.
    ///
    /// Ollama has no API to download blobs, but it can push a model to any registry. osync asks the source
    /// server to push to this relay (insecure/plain HTTP); every blob that arrives is streamed straight into
    /// the destination server's POST /api/blobs/&lt;digest&gt; (nothing is written to disk, memory use is bounded
    /// by the pipe buffer), blobs the destination already has are skipped, and the manifest is kept so the
    /// destination can pull it back (it finds every blob locally and only installs the manifest).
    ///
    /// Implements just what Ollama's push/pull clients use: HEAD blob, POST upload, PATCH chunk,
    /// PUT upload completion, PUT/GET/HEAD manifest, and GET blob for the small blobs it relayed (the child manifests a
    /// 0.40+ server pulls for a manifest list). Every response closes the connection.
    /// Requests for any repository other than <see cref="Repository"/> are rejected, and so are connections from
    /// anything but the source and destination servers (OSYNC_RELAY_ALLOW_ANY=1 lifts this, e.g. behind NAT); digests
    /// must be sha256:&lt;64 hex&gt; (they become part of the destination's URL), and once a manifest is stored a
    /// different one is refused.
    /// </summary>
    internal sealed class RegistryRelay : IAsyncDisposable
    {
        private const string ManifestMediaType = "application/vnd.docker.distribution.manifest.v2+json";
        private const int MaxManifestSize = 4 * 1024 * 1024;
        private const int MaxConnections = 64;
        private const int MaxUploads = 64;
        private static readonly TimeSpan HeaderTimeout = TimeSpan.FromSeconds(30);
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromDays(1) };

        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly string _destServer;
        private readonly long _bandwidthLimit;
        private readonly long _bufferSize;
        private readonly ConcurrentDictionary<string, UploadSession> _uploads = new();
        private readonly object _lock = new();
        private readonly HashSet<IPAddress>? _allowedClients; // null: any client (OSYNC_RELAY_ALLOW_ANY)
        private readonly SemaphoreSlim _connections = new(MaxConnections);
        private string? _pendingDigest;
        private Task? _acceptLoop;

        /// <summary>Addresses whose connections were refused because they are neither server.</summary>
        public ConcurrentDictionary<string, bool> RefusedClients { get; } = new();

        /// <summary>host[:port] under which the servers reach this relay (port omitted when it is 80).</summary>
        public string Authority { get; }

        /// <summary>The only repository this relay accepts, e.g. osync/relay-1a2b3c4d.</summary>
        public string Repository { get; }

        public byte[]? Manifest { get; private set; }
        public string ManifestContentType { get; private set; } = ManifestMediaType;

        /// <summary>Digests streamed into the destination, and digests the destination already had.</summary>
        public ConcurrentBag<string> Transferred { get; } = new();
        public ConcurrentBag<string> Skipped { get; } = new();

        /// <summary>
        /// Contents of the small blobs that went through the relay (config, template, parameters, xOllama settings, ...),
        /// by digest: a model recreated from its manifest takes these parts verbatim.
        /// </summary>
        public ConcurrentDictionary<string, byte[]> SmallBlobs { get; } = new(StringComparer.OrdinalIgnoreCase);

        public const int SmallBlobLimit = 1024 * 1024;

        /// <summary>Sizes of the blobs the destination accepted through the relay, by digest.</summary>
        public ConcurrentDictionary<string, long> BlobSizes { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Digests the relay asks the source to upload even when the destination already has them, so that their
        /// contents reach <see cref="SmallBlobs"/> (set before a second push).
        /// </summary>
        public ConcurrentDictionary<string, bool> ForceUpload { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>First error that happened while forwarding a blob, if any.</summary>
        public string? ForwardError { get; private set; }

        private RegistryRelay(TcpListener listener, string authority, string destServer, long bandwidthLimit, long bufferSize,
            HashSet<IPAddress>? allowedClients)
        {
            _listener = listener;
            Authority = authority;
            _allowedClients = allowedClients;
            Repository = "osync/relay-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            _destServer = destServer.TrimEnd('/');
            _bandwidthLimit = bandwidthLimit;
            _bufferSize = Math.Max(1024 * 1024, bufferSize);
        }

        /// <summary>
        /// Starts a relay reachable from <paramref name="sourceServer"/>. The advertised address is the local
        /// address used to route to the source - or to the destination when the source runs on this machine, so
        /// both can connect - (override with OSYNC_RELAY_HOST, e.g. behind NAT). The port is <paramref name="port"/> when
        /// given, else OSYNC_RELAY_PORT, else a free ephemeral port the OS assigns (above 1024; binding port 0 picks one
        /// atomically, so no other program can take it between a check and the bind). The relay listens only on the
        /// advertised address (both servers connect to it, it is in the model name), or on every interface when
        /// OSYNC_RELAY_HOST names an address that may not be local.
        /// </summary>
        public static RegistryRelay Start(string sourceServer, string destServer, long bandwidthLimit, long bufferSize, int? port = null)
        {
            var overrideHost = Environment.GetEnvironmentVariable("OSYNC_RELAY_HOST");
            IPAddress advertised;
            if (!string.IsNullOrWhiteSpace(overrideHost))
            {
                advertised = Dns.GetHostAddresses(overrideHost.Trim()).First(a => a.AddressFamily == AddressFamily.InterNetwork);
            }
            else
            {
                advertised = LocalAddressTowards(sourceServer);
                if (IPAddress.IsLoopback(advertised))
                    advertised = LocalAddressTowards(destServer);
            }

            var bindAddress = string.IsNullOrWhiteSpace(overrideHost) ? advertised : IPAddress.Any;
            var allowAny = Environment.GetEnvironmentVariable("OSYNC_RELAY_ALLOW_ANY") is "1" or "true";
            var allowed = allowAny ? null : AllowedClients(advertised, sourceServer, destServer);
            var listenPort = port
                ?? (int.TryParse(Environment.GetEnvironmentVariable("OSYNC_RELAY_PORT"), out var configured) ? configured : 0);

            var listener = new TcpListener(bindAddress, listenPort);
            try
            {
                listener.Start();
            }
            catch (SocketException ex)
            {
                throw new InvalidOperationException($"Could not open relay port {listenPort}: {ex.Message}");
            }

            // On port 80 the name carries no port (the registry client's default for http)
            var boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var authority = boundPort == 80 ? advertised.ToString() : $"{advertised}:{boundPort}";
            var relay = new RegistryRelay(listener, authority, destServer, bandwidthLimit, bufferSize, allowed);
            relay._acceptLoop = Task.Run(relay.AcceptLoopAsync);
            return relay;
        }

        /// <summary>Model reference for the relay, e.g. 192.168.1.5/osync/relay-1a2b3c4d:latest.</summary>
        public string ModelName(string tag = "latest") => $"{Authority}/{Repository}:{tag}";

        /// <summary>
        /// The addresses the servers connect from: theirs, plus this machine's (advertised and loopback) for a server
        /// running here, which connects to the advertised address from that same address.
        /// </summary>
        internal static HashSet<IPAddress> AllowedClients(IPAddress advertised, params string[] servers)
        {
            var allowed = new HashSet<IPAddress> { advertised, IPAddress.Loopback };
            foreach (var server in servers)
                foreach (var address in Dns.GetHostAddresses(new Uri(server).Host))
                    allowed.Add(Normalize(address));
            return allowed;
        }

        private static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        /// <summary>A blob digest as registries send it: sha256 and 64 hex digits (it is put in the destination's URL).</summary>
        internal static bool IsValidDigest(string? digest) =>
            digest is { Length: 71 } && digest.StartsWith("sha256:", StringComparison.Ordinal) &&
            digest.AsSpan(7).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

        /// <summary>Text to add to a push error: what the relay refused or failed to forward.</summary>
        public string? Diagnostics()
        {
            var parts = new List<string>();
            if (ForwardError != null) parts.Add($"relay: {ForwardError}");
            if (!RefusedClients.IsEmpty)
                parts.Add($"the relay refused connections from {string.Join(", ", RefusedClients.Keys)}, which is neither server; " +
                          "set OSYNC_RELAY_ALLOW_ANY=1 if a server connects through another address");
            return parts.Count > 0 ? string.Join("; ", parts) : null;
        }

        /// <summary>Local IPv4 address used to reach <paramref name="serverUrl"/> (loopback for local servers).</summary>
        private static IPAddress LocalAddressTowards(string serverUrl)
        {
            var uri = new Uri(serverUrl);
            var target = Dns.GetHostAddresses(uri.Host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? throw new InvalidOperationException($"No IPv4 address for {uri.Host}; set OSYNC_RELAY_HOST");
            if (IPAddress.IsLoopback(target)) return IPAddress.Loopback;

            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect(target, uri.Port > 0 ? uri.Port : 11434); // UDP connect only selects the route, sends nothing
            return ((IPEndPoint)probe.LocalEndPoint!).Address;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch
                {
                    return;
                }
                var remote = Normalize(((IPEndPoint)client.Client.RemoteEndPoint!).Address);
                if (_allowedClients != null && !_allowedClients.Contains(remote))
                {
                    RefusedClients.TryAdd(remote.ToString(), true);
                    client.Dispose();
                    continue;
                }
                if (!_connections.Wait(0))
                {
                    client.Dispose();
                    continue;
                }
                _ = Task.Run(async () =>
                {
                    try { await HandleConnectionAsync(client); }
                    finally { _connections.Release(); }
                });
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();
                    HttpRequest? request;
                    using (var headerTimeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                    {
                        headerTimeout.CancelAfter(HeaderTimeout);
                        try
                        {
                            request = await HttpRequest.ReadAsync(stream, headerTimeout.Token);
                        }
                        catch (OperationCanceledException) when (!_cts.IsCancellationRequested)
                        {
                            return; // a connection that sends no request
                        }
                    }
                    if (request == null) return;
                    await RouteAsync(request, stream);
                }
                catch (Exception ex)
                {
                    lock (_lock) ForwardError ??= ex.Message;
                }
            }
        }

        private async Task RouteAsync(HttpRequest req, NetworkStream stream)
        {
            var path = req.Path;
            if (path is "/v2" or "/v2/")
            {
                await req.DrainBodyAsync(_cts.Token);
                await WriteResponseAsync(stream, 200, "OK", body: "{}"u8.ToArray(), contentType: "application/json");
                return;
            }

            var prefix = $"/v2/{Repository}/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
            {
                await req.DrainBodyAsync(_cts.Token);
                await WriteResponseAsync(stream, 404, "Not Found");
                return;
            }
            var rest = path[prefix.Length..];

            if (rest.StartsWith("blobs/uploads", StringComparison.Ordinal))
            {
                var id = rest.Length > "blobs/uploads/".Length ? rest["blobs/uploads/".Length..].Trim('/') : "";
                switch (req.Method)
                {
                    case "POST" when id.Length == 0:
                        await StartUploadAsync(req, stream);
                        return;
                    case "PATCH":
                        await PatchUploadAsync(req, stream, id);
                        return;
                    case "PUT":
                        await CompleteUploadAsync(req, stream, id);
                        return;
                }
            }
            else if (rest.StartsWith("blobs/", StringComparison.Ordinal))
            {
                var digest = rest["blobs/".Length..];
                if (req.Method == "HEAD" && !IsValidDigest(digest))
                {
                    await WriteResponseAsync(stream, 400, "Bad Request");
                    return;
                }
                if (req.Method == "HEAD")
                {
                    await BlobExistsAsync(stream, digest);
                    return;
                }
                if (req.Method == "GET")
                {
                    await GetBlobAsync(stream, digest);
                    return;
                }
            }
            else if (rest.StartsWith("manifests/", StringComparison.Ordinal))
            {
                if (req.Method == "PUT")
                {
                    await PutManifestAsync(req, stream);
                    return;
                }
                if (req.Method is "GET" or "HEAD")
                {
                    await GetManifestAsync(stream, head: req.Method == "HEAD");
                    return;
                }
            }

            await req.DrainBodyAsync(_cts.Token);
            await WriteResponseAsync(stream, 404, "Not Found");
        }

        /// <summary>HEAD blob: the blob "exists" when the destination already has it, so the source skips it.</summary>
        private async Task BlobExistsAsync(NetworkStream stream, string digest)
        {
            bool exists;
            using (var head = new HttpRequestMessage(HttpMethod.Head, $"{_destServer}/api/blobs/{digest}"))
            using (var response = await Http.SendAsync(head, _cts.Token))
            {
                exists = response.IsSuccessStatusCode;
            }

            if (exists && !ForceUpload.ContainsKey(digest))
            {
                Skipped.Add(digest);
                // A pull of a manifest list (Ollama 0.40+) sizes the child manifest from this answer and downloads it
                // unless the destination has a blob of that size
                long? size = BlobSizes.TryGetValue(digest, out var known) ? known : null;
                await WriteResponseAsync(stream, 200, "OK", new() { ["Docker-Content-Digest"] = digest }, contentLength: size);
            }
            else
            {
                // Ollama's push client checks a blob and then uploads it without naming the digest again
                lock (_lock) _pendingDigest = digest;
                await WriteResponseAsync(stream, 404, "Not Found");
            }
        }

        /// <summary>GET blob: only the small blobs that went through the relay (a manifest list's child manifests).</summary>
        private async Task GetBlobAsync(NetworkStream stream, string digest)
        {
            if (!IsValidDigest(digest) || !SmallBlobs.TryGetValue(digest, out var data))
            {
                await WriteResponseAsync(stream, 404, "Not Found");
                return;
            }
            await WriteResponseAsync(stream, 200, "OK", new() { ["Docker-Content-Digest"] = digest },
                body: data, contentType: "application/octet-stream");
        }

        private async Task StartUploadAsync(HttpRequest req, NetworkStream stream)
        {
            await req.DrainBodyAsync(_cts.Token);
            string? digest = req.Query("digest");
            if (digest != null && !IsValidDigest(digest))
            {
                await WriteResponseAsync(stream, 400, "Bad Request");
                return;
            }
            if (_uploads.Count >= MaxUploads)
            {
                await WriteResponseAsync(stream, 429, "Too Many Requests");
                return;
            }
            if (digest == null)
            {
                lock (_lock)
                {
                    digest = _pendingDigest;
                    _pendingDigest = null;
                }
            }

            var id = Guid.NewGuid().ToString("N");
            _uploads[id] = new UploadSession(digest);
            // 202 (not 201) also for "?mount=" requests: the relay never mounts, the blob is always uploaded
            await WriteResponseAsync(stream, 202, "Accepted", UploadHeaders(id, 0));
        }

        private async Task PatchUploadAsync(HttpRequest req, NetworkStream stream, string id)
        {
            if (!_uploads.TryGetValue(id, out var session))
            {
                await req.DrainBodyAsync(_cts.Token);
                await WriteResponseAsync(stream, 404, "Not Found");
                return;
            }

            long start = session.Received;
            var range = req.Header("Content-Range");
            if (range != null)
            {
                var value = range.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase) ? range[6..] : range;
                var dash = value.IndexOf('-');
                if (dash > 0 && long.TryParse(value[..dash], out var parsed)) start = parsed;
            }

            if (start > session.Received)
            {
                // A gap: the client skipped data the relay never received
                await req.DrainBodyAsync(_cts.Token);
                await WriteResponseAsync(stream, 416, "Range Not Satisfiable", UploadHeaders(id, session.Received));
                return;
            }

            try
            {
                await session.WriteAsync(req, start, this);
            }
            catch (Exception ex)
            {
                lock (_lock) ForwardError ??= ex.Message;
                await WriteResponseAsync(stream, 500, "Internal Server Error", body: Encoding.UTF8.GetBytes(ex.Message));
                return;
            }
            await WriteResponseAsync(stream, 202, "Accepted", UploadHeaders(id, session.Received));
        }

        private async Task CompleteUploadAsync(HttpRequest req, NetworkStream stream, string id)
        {
            if (!_uploads.TryRemove(id, out var session))
            {
                await req.DrainBodyAsync(_cts.Token);
                await WriteResponseAsync(stream, 404, "Not Found");
                return;
            }

            var digest = req.Query("digest") ?? session.Digest;
            try
            {
                if (digest == null) throw new InvalidOperationException("upload completed without a digest");
                if (!IsValidDigest(digest)) throw new InvalidOperationException($"invalid digest '{digest}'");
                if (session.Digest != null && !string.Equals(session.Digest, digest, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"upload for {session.Digest} completed as {digest}");
                session.Digest = digest;

                // A monolithic upload may carry (the rest of) the blob in the PUT body
                await session.WriteAsync(req, session.Received, this);
                await session.CompleteAsync(this);
                Transferred.Add(digest);
            }
            catch (Exception ex)
            {
                lock (_lock) ForwardError ??= ex.Message;
                await WriteResponseAsync(stream, 400, "Bad Request", body: Encoding.UTF8.GetBytes(ex.Message));
                return;
            }

            await WriteResponseAsync(stream, 201, "Created", new()
            {
                ["Location"] = $"http://{Authority}/v2/{Repository}/blobs/{digest}",
                ["Docker-Content-Digest"] = digest
            });
        }

        private async Task PutManifestAsync(HttpRequest req, NetworkStream stream)
        {
            if (long.TryParse(req.Header("Content-Length"), out var length) && length > MaxManifestSize)
            {
                await WriteResponseAsync(stream, 413, "Payload Too Large");
                return;
            }
            using var body = new MemoryStream();
            await req.ReadBodyAsync(chunk =>
            {
                if (body.Length + chunk.Length > MaxManifestSize) throw new InvalidOperationException("manifest too large");
                body.Write(chunk.Span);
                return Task.CompletedTask;
            }, _cts.Token);
            var manifest = body.ToArray();

            bool accepted;
            lock (_lock)
            {
                // Every push of the model sends the same manifest: a different one would replace the model being copied
                accepted = Manifest == null || Manifest.AsSpan().SequenceEqual(manifest);
                if (accepted && Manifest == null)
                {
                    Manifest = manifest;
                    ManifestContentType = req.Header("Content-Type") ?? ManifestMediaType;
                }
            }
            if (!accepted)
            {
                await WriteResponseAsync(stream, 409, "Conflict");
                return;
            }
            await WriteResponseAsync(stream, 201, "Created", new() { ["Docker-Content-Digest"] = ManifestDigest() });
        }

        private async Task GetManifestAsync(NetworkStream stream, bool head)
        {
            if (Manifest == null)
            {
                await WriteResponseAsync(stream, 404, "Not Found");
                return;
            }
            await WriteResponseAsync(stream, 200, "OK", new() { ["Docker-Content-Digest"] = ManifestDigest() },
                body: Manifest, contentType: ManifestContentType, headOnly: head);
        }

        private string ManifestDigest() => "sha256:" + Convert.ToHexString(SHA256.HashData(Manifest!)).ToLowerInvariant();

        private Dictionary<string, string> UploadHeaders(string id, long received) => new()
        {
            // Ollama's client needs an absolute Location for every upload step
            ["Location"] = $"http://{Authority}/v2/{Repository}/blobs/uploads/{id}",
            ["Docker-Upload-UUID"] = id,
            ["Range"] = received > 0 ? $"0-{received - 1}" : "0-0"
        };

        private static async Task WriteResponseAsync(NetworkStream stream, int status, string reason,
            Dictionary<string, string>? headers = null, byte[]? body = null, string? contentType = null, bool headOnly = false,
            long? contentLength = null)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n");
            sb.Append("Connection: close\r\n");
            sb.Append("Docker-Distribution-API-Version: registry/2.0\r\n");
            if (contentType != null) sb.Append("Content-Type: ").Append(contentType).Append("\r\n");
            // contentLength: the size of a resource a HEAD answer describes without a body
            sb.Append("Content-Length: ").Append(contentLength ?? body?.Length ?? 0).Append("\r\n");
            if (headers != null)
                foreach (var (name, value) in headers)
                    sb.Append(name).Append(": ").Append(value).Append("\r\n");
            sb.Append("\r\n");
            await stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()));
            if (body != null && !headOnly) await stream.WriteAsync(body);
            await stream.FlushAsync();
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            try { _listener.Stop(); } catch { /* already stopped */ }
            if (_acceptLoop != null)
            {
                try { await _acceptLoop; } catch { /* cancelled */ }
            }
            foreach (var session in _uploads.Values) await session.AbortAsync();
            _uploads.Clear();
        }

        /// <summary>One blob upload: bytes are forwarded to the destination as they arrive.</summary>
        private sealed class UploadSession
        {
            private Pipe? _pipe;
            private Stream? _writer;
            private Task<HttpResponseMessage>? _destination;
            private FileStream? _spool; // used only when the digest is not known before the data arrives
            private MemoryStream? _copy = new(); // the data of a small blob, dropped past SmallBlobLimit

            public UploadSession(string? digest) => Digest = digest;

            public string? Digest { get; set; }

            /// <summary>Bytes received and forwarded so far (the next expected offset).</summary>
            public long Received { get; private set; }

            public async Task WriteAsync(HttpRequest req, long start, RegistryRelay relay)
            {
                // A retried chunk resends bytes that were already forwarded: skip them
                long skip = Received - start;
                await req.ReadBodyAsync(async chunk =>
                {
                    if (skip > 0)
                    {
                        if (chunk.Length <= skip)
                        {
                            skip -= chunk.Length;
                            return;
                        }
                        chunk = chunk[(int)skip..];
                        skip = 0;
                    }
                    await TargetFor(relay).WriteAsync(chunk, relay._cts.Token);
                    Received += chunk.Length;
                    if (_copy != null && Received <= SmallBlobLimit) _copy.Write(chunk.Span);
                    else _copy = null;
                }, relay._cts.Token);
            }

            private Stream TargetFor(RegistryRelay relay)
            {
                if (_writer != null) return _writer;
                if (Digest == null)
                {
                    _spool = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                        81920, FileOptions.DeleteOnClose);
                    return _writer = _spool;
                }
                return _writer = OpenDestination(relay, Digest);
            }

            private Stream OpenDestination(RegistryRelay relay, string digest)
            {
                _pipe = new Pipe(new PipeOptions(
                    pauseWriterThreshold: relay._bufferSize,
                    resumeWriterThreshold: relay._bufferSize / 2));
                var content = new StreamContent(_pipe.Reader.AsStream());
                _destination = Http.PostAsync($"{relay._destServer}/api/blobs/{digest}", content, relay._cts.Token);
                Stream writer = _pipe.Writer.AsStream();
                return relay._bandwidthLimit > 0 ? new ThrottledStream(writer, relay._bandwidthLimit) : writer;
            }

            public async Task CompleteAsync(RegistryRelay relay)
            {
                if (_spool != null)
                {
                    // Digest became known only now: send the spooled data
                    _spool.Position = 0;
                    var destination = OpenDestination(relay, Digest!);
                    await _spool.CopyToAsync(destination, relay._cts.Token);
                    await _spool.DisposeAsync();
                    _spool = null;
                    _writer = destination;
                }
                else if (_writer == null)
                {
                    // Empty blob
                    OpenDestination(relay, Digest!);
                }

                await _pipe!.Writer.CompleteAsync();
                using var response = await _destination!;
                if (!response.IsSuccessStatusCode)
                {
                    var text = await response.Content.ReadAsStringAsync();
                    throw new InvalidOperationException($"destination rejected blob {Digest}: {(int)response.StatusCode} {text}".Trim());
                }

                relay.BlobSizes[Digest!] = Received;
                if (_copy != null)
                {
                    var data = _copy.ToArray();
                    if (string.Equals(Digest, "sha256:" + Convert.ToHexString(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase))
                        relay.SmallBlobs[Digest!] = data;
                }
            }

            public async Task AbortAsync()
            {
                if (_pipe != null) await _pipe.Writer.CompleteAsync(new OperationCanceledException());
                if (_spool != null) await _spool.DisposeAsync();
            }
        }

        /// <summary>A parsed HTTP/1.1 request whose body is read on demand (Content-Length or chunked).</summary>
        private sealed class HttpRequest
        {
            private readonly NetworkStream _stream;
            private byte[] _buffer;
            private int _start;
            private int _end;
            private bool _bodyRead;

            private HttpRequest(NetworkStream stream, byte[] buffer, int start, int end)
            {
                _stream = stream;
                _buffer = buffer;
                _start = start;
                _end = end;
            }

            public string Method { get; private set; } = "";
            public string Path { get; private set; } = "";
            private string _query = "";
            private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

            public string? Header(string name) => _headers.TryGetValue(name, out var v) ? v : null;

            public string? Query(string name)
            {
                foreach (var part in _query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = part.IndexOf('=');
                    var key = eq < 0 ? part : part[..eq];
                    if (key == name) return eq < 0 ? "" : Uri.UnescapeDataString(part[(eq + 1)..]);
                }
                return null;
            }

            public static async Task<HttpRequest?> ReadAsync(NetworkStream stream, CancellationToken ct)
            {
                var buffer = new byte[64 * 1024];
                int end = 0;
                while (true)
                {
                    var headerEnd = IndexOfHeaderEnd(buffer, end);
                    if (headerEnd >= 0)
                    {
                        var request = new HttpRequest(stream, buffer, headerEnd + 4, end);
                        request.ParseHead(Encoding.ASCII.GetString(buffer, 0, headerEnd));
                        return request;
                    }
                    if (end == buffer.Length) throw new InvalidOperationException("request header too large");
                    var read = await stream.ReadAsync(buffer.AsMemory(end), ct);
                    if (read == 0) return null;
                    end += read;
                }
            }

            private static int IndexOfHeaderEnd(byte[] buffer, int length)
            {
                for (int i = 0; i + 3 < length; i++)
                    if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                        return i;
                return -1;
            }

            private void ParseHead(string head)
            {
                var lines = head.Split("\r\n");
                var requestLine = lines[0].Split(' ');
                Method = requestLine[0].ToUpperInvariant();
                var target = requestLine.Length > 1 ? requestLine[1] : "/";
                if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    target = new Uri(target).PathAndQuery;
                var q = target.IndexOf('?');
                Path = Uri.UnescapeDataString(q < 0 ? target : target[..q]);
                _query = q < 0 ? "" : target[(q + 1)..];
                foreach (var line in lines.Skip(1))
                {
                    var colon = line.IndexOf(':');
                    if (colon > 0) _headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
                }
            }

            public Task DrainBodyAsync(CancellationToken ct) => ReadBodyAsync(_ => Task.CompletedTask, ct);

            /// <summary>Delivers the body in chunks as it arrives from the socket.</summary>
            public async Task ReadBodyAsync(Func<ReadOnlyMemory<byte>, Task> sink, CancellationToken ct)
            {
                if (_bodyRead) return;
                _bodyRead = true;

                if (string.Equals(Header("Transfer-Encoding"), "chunked", StringComparison.OrdinalIgnoreCase))
                {
                    while (true)
                    {
                        var sizeLine = await ReadLineAsync(ct);
                        var semicolon = sizeLine.IndexOf(';');
                        var size = Convert.ToInt64(semicolon < 0 ? sizeLine : sizeLine[..semicolon], 16);
                        if (size == 0)
                        {
                            while ((await ReadLineAsync(ct)).Length > 0) { } // trailers
                            return;
                        }
                        await ReadExactlyAsync(size, sink, ct);
                        await ReadLineAsync(ct); // CRLF after the chunk
                    }
                }

                var length = long.TryParse(Header("Content-Length"), out var l) ? l : 0;
                await ReadExactlyAsync(length, sink, ct);
            }

            private async Task ReadExactlyAsync(long count, Func<ReadOnlyMemory<byte>, Task> sink, CancellationToken ct)
            {
                while (count > 0)
                {
                    if (_start == _end)
                    {
                        _start = 0;
                        _end = await _stream.ReadAsync(_buffer, ct);
                        if (_end == 0) throw new EndOfStreamException("connection closed before the request body was complete");
                    }
                    var take = (int)Math.Min(count, _end - _start);
                    await sink(_buffer.AsMemory(_start, take));
                    _start += take;
                    count -= take;
                }
            }

            private async Task<string> ReadLineAsync(CancellationToken ct)
            {
                var line = new StringBuilder();
                while (true)
                {
                    if (_start == _end)
                    {
                        _start = 0;
                        _end = await _stream.ReadAsync(_buffer, ct);
                        if (_end == 0) throw new EndOfStreamException();
                    }
                    var c = (char)_buffer[_start++];
                    if (c == '\n') return line.ToString().TrimEnd('\r');
                    line.Append(c);
                }
            }
        }
    }
}
