using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;

namespace osync.Tests.UnitTests;

/// <summary>
/// The relay's request checks, against a relay on loopback (no Ollama needed: requests that pass the checks would
/// reach the destination, so every case here must be refused before that).
/// </summary>
public class RegistryRelayTests
{
    // Nothing listens there: a request that got past the checks would fail differently (502/500, not 400/409/413)
    private const string NoServer = "http://127.0.0.1:9";

    [Theory]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", true)]
    [InlineData("sha256:0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", true)]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcde", false)]
    [InlineData("sha256:../../api/pull", false)]
    [InlineData("../pull", false)]
    [InlineData("sha256:0123456789abcdef0123456789abcdef0123456789abcdef012345678/../x", false)]
    [InlineData("sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidDigest_AcceptsOnlySha256Hex(string? digest, bool valid)
    {
        RegistryRelay.IsValidDigest(digest).Should().Be(valid);
    }

    [Fact]
    public void AllowedClients_AreTheServersAndThisMachine()
    {
        var advertised = IPAddress.Parse("192.168.1.5");
        var allowed = RegistryRelay.AllowedClients(advertised, "http://10.0.0.7:11434", "http://127.0.0.1:11434");

        allowed.Should().BeEquivalentTo(new[] { advertised, IPAddress.Loopback, IPAddress.Parse("10.0.0.7") });
    }

    [Fact]
    public async Task HeadBlob_WithTraversalDigest_IsRejected()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);

        var status = await SendAsync(relay, "HEAD", $"/v2/{relay.Repository}/blobs/..%2F..%2Fapi%2Ftags");

        status.Should().Be(400);
    }

    [Fact]
    public async Task StartUpload_WithTraversalDigest_IsRejected()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);

        var status = await SendAsync(relay, "POST", $"/v2/{relay.Repository}/blobs/uploads/?digest=..%2Fpull");

        status.Should().Be(400);
    }

    [Fact]
    public async Task CompleteUpload_WithTraversalDigest_IsRejected()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);
        var location = await StartUploadAsync(relay);

        var status = await SendAsync(relay, "PUT", $"{location}?digest=..%2Fcreate", Encoding.UTF8.GetBytes("{}"));

        status.Should().Be(400);
        relay.Transferred.Should().BeEmpty();
    }

    [Fact]
    public async Task Manifest_OnceStored_CannotBeReplaced()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);
        var path = $"/v2/{relay.Repository}/manifests/latest";
        var first = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"layers\":[]}");
        var other = Encoding.UTF8.GetBytes("{\"schemaVersion\":2,\"layers\":[{}]}");

        (await SendAsync(relay, "PUT", path, first)).Should().Be(201);
        (await SendAsync(relay, "PUT", path, other)).Should().Be(409);
        (await SendAsync(relay, "PUT", path, first)).Should().Be(201, "a second push sends the same manifest");

        relay.Manifest.Should().Equal(first);
    }

    [Fact]
    public async Task Manifest_TooLarge_IsRejected()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);

        var status = await SendAsync(relay, "PUT", $"/v2/{relay.Repository}/manifests/latest", new byte[5 * 1024 * 1024]);

        status.Should().BeOneOf(413, -1); // -1: the relay closed the connection while the body was still being sent
        relay.Manifest.Should().BeNull();
    }

    [Fact]
    public async Task OtherRepository_IsNotFound()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);

        var status = await SendAsync(relay, "GET", "/v2/library/llama3/manifests/latest");

        status.Should().Be(404);
    }

    [Fact]
    public async Task GetBlob_ServesOnlyBlobsTheRelayForwarded()
    {
        await using var relay = RegistryRelay.Start(NoServer, NoServer, 0, 0);
        var data = Encoding.UTF8.GetBytes("{\"schemaVersion\":2}");
        var digest = "sha256:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();
        relay.SmallBlobs[digest] = data;

        (await SendAsync(relay, "GET", $"/v2/{relay.Repository}/blobs/{digest}")).Should().Be(200);
        (await SendAsync(relay, "GET", $"/v2/{relay.Repository}/blobs/sha256:{new string('0', 64)}")).Should().Be(404);
        (await SendAsync(relay, "GET", $"/v2/{relay.Repository}/blobs/..%2F..%2Fapi%2Ftags")).Should().Be(404);
    }

    private static async Task<string> StartUploadAsync(RegistryRelay relay)
    {
        var (status, headers) = await SendRawAsync(relay, "POST", $"/v2/{relay.Repository}/blobs/uploads/", null);
        status.Should().Be(202);
        return new Uri(headers["Location"]).AbsolutePath;
    }

    private static async Task<int> SendAsync(RegistryRelay relay, string method, string target, byte[]? body = null) =>
        (await SendRawAsync(relay, method, target, body)).Status;

    /// <summary>Raw HTTP/1.1, so paths reach the relay exactly as written (HttpClient would normalize them).</summary>
    private static async Task<(int Status, Dictionary<string, string> Headers)> SendRawAsync(
        RegistryRelay relay, string method, string target, byte[]? body)
    {
        var authority = relay.Authority.Contains(':') ? relay.Authority : relay.Authority + ":80";
        var colon = authority.LastIndexOf(':');
        using var client = new TcpClient();
        await client.ConnectAsync(authority[..colon], int.Parse(authority[(colon + 1)..]));
        var stream = client.GetStream();
        try
        {
            var head = $"{method} {target} HTTP/1.1\r\nHost: {relay.Authority}\r\nContent-Length: {body?.Length ?? 0}\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            if (body != null) await stream.WriteAsync(body);
        }
        catch (IOException)
        {
            return (-1, new());
        }

        using var reader = new StreamReader(stream, Encoding.ASCII);
        string? statusLine;
        try
        {
            statusLine = await reader.ReadLineAsync();
        }
        catch (IOException)
        {
            return (-1, new());
        }
        if (statusLine == null) return (-1, new());

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
        {
            var sep = line.IndexOf(':');
            if (sep > 0) headers[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }
        return (int.Parse(statusLine.Split(' ')[1]), headers);
    }
}
