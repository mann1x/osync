using System.Net.Http.Json;
using System.Text.Json;

namespace osync
{
    /// <summary>The source server could not deliver the model to the relay (typically it cannot reach this machine).</summary>
    internal sealed class RelayUnreachableException : Exception
    {
        public RelayUnreachableException(string message) : base(message) { }
    }

    /// <summary>
    /// Copies a model from one Ollama/xOllama server to another through a <see cref="RegistryRelay"/>:
    ///   1. source: /api/copy the model to a temporary name that points at the relay
    ///   2. source: /api/push it (insecure) - the relay streams each blob into the destination's /api/blobs
    ///   3. destination: /api/pull the temporary name - all blobs are already there, only the manifest is fetched
    ///   4. destination: /api/copy to the final name, then delete the temporary names on both servers
    ///   5. both: compare /api/show, so a copy that lost a part is reported as a failure
    /// Works for any model the source has (registry, created, imported), keeps its manifest byte-for-byte
    /// (all layers: template, params, license, messages, projector, draft, ...) and needs no internet access.
    /// When the destination cannot pull the manifest (it cannot reach this machine, or it runs on Windows and the
    /// relay is not on port 80), the model is recreated from the manifest with /api/create (see ModelRecreate).
    /// </summary>
    internal static class RelayCopy
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromDays(1) };

        /// <summary>Copies the model and returns how it was installed on the destination (for the final message).</summary>
        public static async Task<string> CopyAsync(string sourceServer, string sourceModel, string destServer, string destModel,
            long bandwidthLimit, long bufferSize)
        {
            sourceServer = sourceServer.TrimEnd('/');
            destServer = destServer.TrimEnd('/');

            using var sourceShow = await ShowAsync(sourceServer, sourceModel)
                ?? throw new InvalidOperationException($"model '{sourceModel}' not found on {sourceServer}");

            if (string.Equals(sourceServer, destServer, StringComparison.OrdinalIgnoreCase))
            {
                // Same server: a plain copy is enough
                await PostAsync(destServer, "/api/copy", new { source = sourceModel, destination = destModel });
                return "on the same server";
            }

            RegistryRelay startedRelay;
            try
            {
                startedRelay = RegistryRelay.Start(sourceServer, destServer, bandwidthLimit, bufferSize);
            }
            catch (Exception ex)
            {
                throw new RelayUnreachableException(ex.Message);
            }
            await using var relay = startedRelay;
            var tempName = relay.ModelName();
            Console.WriteLine($"Relay listening at {relay.Authority} (source pushes, destination receives)");

            await PostAsync(sourceServer, "/api/copy", new { source = sourceModel, destination = tempName });
            try
            {
                return await PushAndInstallAsync(sourceServer, tempName, sourceShow, destServer, destModel, relay);
            }
            finally
            {
                await TryDeleteAsync(sourceServer, tempName);
            }
        }

        private static async Task<string> PushAndInstallAsync(string sourceServer, string tempName, JsonDocument sourceShow,
            string destServer, string destModel, RegistryRelay relay)
        {
            await PushAsync(sourceServer, tempName, relay);
            if (relay.Manifest == null)
                throw new InvalidOperationException("the source server did not push a manifest");
            var manifest = relay.Manifest;

            Console.WriteLine($"Installing '{destModel}' on {destServer}...");
            string method;
            try
            {
                // OSYNC_RELAY_INSTALL=create skips the manifest pull (e.g. for destinations that cannot reach this machine)
                if (string.Equals(Environment.GetEnvironmentVariable("OSYNC_RELAY_INSTALL"), "create", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("OSYNC_RELAY_INSTALL=create");
                await PostAsync(destServer, "/api/pull", new { model = tempName, insecure = true, stream = false });
                await PostAsync(destServer, "/api/copy", new { source = tempName, destination = destModel });
                method = "through the relay";
            }
            catch (Exception ex)
            {
                // The blobs are already on the destination; only the manifest could not be fetched from the relay.
                // Recreate the model from its manifest instead.
                if (IsWindowsManifestPathError(ex.Message, relay.Authority))
                    Console.WriteLine("The destination cannot store a manifest from the relay (Windows does not allow ':' in folder names).");
                else
                    Out.Warning($"the destination could not install the manifest from the relay ({ex.Message}).");
                Console.WriteLine("Recreating the model from its manifest on the destination...");
                await FetchSmallBlobsAsync(sourceServer, tempName, relay, manifest);
                var request = ModelRecreate.BuildCreateRequest(destModel, manifest,
                    digest => relay.SmallBlobs.TryGetValue(digest, out var data) ? data : null, sourceShow.RootElement);
                await PostAsync(destServer, "/api/create", request);
                method = "recreated from its manifest";
            }
            finally
            {
                await TryDeleteAsync(destServer, tempName);
            }

            Console.WriteLine($"Transferred {relay.Transferred.Count} blob(s), {relay.Skipped.Count} already present on the destination");
            await VerifyAsync(sourceShow.RootElement, destServer, destModel);
            return $"{method}, verified";
        }

        /// <summary>
        /// The recreate takes the config and the small layers (template, parameters, xOllama settings, ...) verbatim, so
        /// that /api/create writes blobs with the source's digests. The relay saw the ones it forwarded; the ones the
        /// destination already had were skipped, so the source pushes again and the relay asks for exactly those.
        /// </summary>
        private static async Task FetchSmallBlobsAsync(string sourceServer, string tempName, RegistryRelay relay, byte[] manifest)
        {
            foreach (var digest in ModelRecreate.InlineBlobs(manifest, RegistryRelay.SmallBlobLimit))
                if (!relay.SmallBlobs.ContainsKey(digest))
                    relay.ForceUpload[digest] = true;
            if (relay.ForceUpload.IsEmpty) return;

            try
            {
                await PushAsync(sourceServer, tempName, relay);
            }
            catch (Exception ex)
            {
                // The recreate then takes those parts from /api/show, and the verification reports what differs
                Out.Warning($"could not read the model's settings from the source ({ex.Message}).");
            }
        }

        /// <summary>
        /// /api/pull failing because the manifest path would contain the relay's "host:port": ':' is not allowed in a
        /// Windows folder name, so a Windows destination cannot pull from a relay that is not on port 80.
        /// </summary>
        internal static bool IsWindowsManifestPathError(string message, string relayAuthority) =>
            relayAuthority.Contains(':') &&
            message.Contains(relayAuthority, StringComparison.OrdinalIgnoreCase) &&
            message.Contains("mkdir", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Compares the destination's copy with the source through /api/show and throws when a part is missing or
        /// different (template, parameters, renderer, xOllama settings, ...).
        /// </summary>
        internal static async Task VerifyAsync(JsonElement sourceShow, string destServer, string destModel)
        {
            using var destShow = await ShowAsync(destServer, destModel)
                ?? throw new InvalidOperationException($"'{destModel}' is not on {destServer} after the copy");
            var (lost, derived) = ModelRecreate.CompareShow(sourceShow, destShow.RootElement,
                OllamaServer.GetFlavor(destServer) == ServerFlavor.XOllama);
            if (lost.Count > 0)
                throw new InvalidOperationException(
                    $"the copy of '{destModel}' on {destServer} differs from the source in: {string.Join(", ", lost)}");
            if (derived.Count > 0)
                Out.Warning($"{destServer} reports a different {string.Join(", ", derived)} for '{destModel}' than the source " +
                            "(the model's files and settings match; the servers may be different versions).");
        }

        /// <summary>Runs /api/push on the source and shows its progress; the relay receives the data.</summary>
        private static async Task PushAsync(string sourceServer, string tempName, RegistryRelay relay)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{sourceServer}/api/push")
            {
                Content = JsonContent.Create(new { model = tempName, insecure = true, stream = true })
            };
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"push failed on the source server: {(int)response.StatusCode} {text}".Trim());
            }

            var progress = new PushProgress();
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            string? line;
            bool success = false;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    progress.Finish();
                    var message = error.GetString() ?? "unknown error";
                    if (relay.Transferred.IsEmpty && relay.Skipped.IsEmpty && relay.Manifest == null && LooksLikeConnectivity(message))
                        throw new RelayUnreachableException(message);
                    throw new InvalidOperationException($"push failed: {message}" +
                        (relay.ForwardError != null ? $" (relay: {relay.ForwardError})" : ""));
                }

                var status = root.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";
                if (status == "success") success = true;
                var digest = root.TryGetProperty("digest", out var d) ? d.GetString() : null;
                long total = root.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt64() : 0;
                long completed = root.TryGetProperty("completed", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt64() : 0;
                progress.Report(status, digest, completed, total);
            }
            progress.Finish();

            if (!success)
                throw new InvalidOperationException("push ended without success" +
                    (relay.ForwardError != null ? $" (relay: {relay.ForwardError})" : ""));
        }

        private static bool LooksLikeConnectivity(string message)
        {
            var m = message.ToLowerInvariant();
            return m.Contains("dial tcp") || m.Contains("connection refused") || m.Contains("i/o timeout") ||
                   m.Contains("no route to host") || m.Contains("network is unreachable") || m.Contains("connection reset") ||
                   m.Contains("context deadline exceeded");
        }

        internal static async Task<JsonDocument?> ShowAsync(string server, string model)
        {
            using var response = await Http.PostAsJsonAsync($"{server}/api/show", new { model });
            if (!response.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }

        /// <summary>Parses /api/show "parameters" text ("name value" lines) into a /api/create parameters map.</summary>
        internal static Dictionary<string, object> ParseParameters(string text)
        {
            var result = new Dictionary<string, object>();
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var space = raw.IndexOf(' ');
                if (space < 0) continue;
                var name = raw[..space];
                var valueText = raw[space..].Trim();
                object value;
                if (valueText.Length >= 2 && valueText[0] == '"' && valueText[^1] == '"')
                {
                    try { value = JsonSerializer.Deserialize<string>(valueText) ?? ""; }
                    catch (JsonException) { value = valueText[1..^1]; }
                }
                else if (long.TryParse(valueText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var l)) value = l;
                else if (double.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) value = d;
                else if (bool.TryParse(valueText, out var b)) value = b;
                else value = valueText;

                if (name == "stop")
                {
                    if (!result.TryGetValue("stop", out var list)) result["stop"] = list = new List<object>();
                    ((List<object>)list).Add(value);
                }
                else
                {
                    result[name] = value;
                }
            }
            return result;
        }

        private static async Task PostAsync(string server, string path, object body)
        {
            using var response = await Http.PostAsJsonAsync($"{server}{path}", body);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode || text.Contains("\"error\""))
                throw new InvalidOperationException($"{path} on {server} failed: {(int)response.StatusCode} {ErrorText(text)}".Trim());
        }

        private static string ErrorText(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var e)) return e.GetString() ?? body;
            }
            catch
            {
                // not JSON
            }
            return body;
        }

        private static async Task TryDeleteAsync(string server, string model)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{server}/api/delete")
                {
                    Content = JsonContent.Create(new { model })
                };
                using var _ = await Http.SendAsync(request);
            }
            catch
            {
                // best effort cleanup of the temporary name
            }
        }

        /// <summary>Console progress for the push stream: one line per blob, updated in place on a terminal.</summary>
        private sealed class PushProgress
        {
            private readonly bool _interactive = !Console.IsOutputRedirected;
            private string? _current;
            private long _lastCompleted;
            private DateTime _lastDraw = DateTime.MinValue;
            private DateTime _started = DateTime.UtcNow;

            public void Report(string status, string? digest, long completed, long total)
            {
                if (digest == null || total <= 0) return;
                if (digest != _current)
                {
                    Finish();
                    _current = digest;
                    _started = DateTime.UtcNow;
                    if (!_interactive) Console.WriteLine($"  {Short(digest)}  {Format(total)}");
                }
                _lastCompleted = completed;

                if (_interactive && (DateTime.UtcNow - _lastDraw).TotalMilliseconds >= 200)
                {
                    _lastDraw = DateTime.UtcNow;
                    var seconds = Math.Max(0.001, (DateTime.UtcNow - _started).TotalSeconds);
                    var percent = total > 0 ? completed * 100.0 / total : 0;
                    Console.Write($"\r  {Short(digest)}  {Format(completed)} / {Format(total)}  {percent,5:F1}%  {Format((long)(completed / seconds))}/s   ");
                }
            }

            public void Finish()
            {
                if (_current != null && _interactive) Console.WriteLine();
                _current = null;
                _lastCompleted = 0;
            }

            private static string Short(string digest) => digest.Length > 19 ? digest[..19] : digest;

            private static string Format(long bytes)
            {
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double value = bytes;
                int unit = 0;
                while (value >= 1024 && unit < units.Length - 1)
                {
                    value /= 1024;
                    unit++;
                }
                return $"{value:0.##} {units[unit]}";
            }
        }
    }
}
