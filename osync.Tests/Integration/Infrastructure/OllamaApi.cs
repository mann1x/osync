using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace osync.Tests.Integration.Infrastructure;

/// <summary>
/// Direct Ollama HTTP API access used to arrange and verify test state independently of osync
/// (osync is the system under test, so it is never used to set up or check its own results).
/// </summary>
public sealed class OllamaApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public string BaseUrl { get; }

    public OllamaApi(string baseUrl) => BaseUrl = baseUrl.TrimEnd('/');

    /// <summary>Normalizes a model reference to name:tag (adds :latest when no tag is given).</summary>
    public static string WithTag(string model)
    {
        var lastSlash = model.LastIndexOf('/');
        return model.IndexOf(':', lastSlash + 1) >= 0 ? model : model + ":latest";
    }

    public async Task<List<ModelEntry>> ListAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"{BaseUrl}/api/tags"));
        var result = new List<ModelEntry>();
        foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
        {
            var details = m.TryGetProperty("details", out var d) ? d : default;
            result.Add(new ModelEntry(
                m.GetProperty("name").GetString()!,
                m.GetProperty("size").GetInt64(),
                m.GetProperty("digest").GetString()!,
                details.ValueKind == JsonValueKind.Object && details.TryGetProperty("quantization_level", out var q) ? q.GetString() : null,
                details.ValueKind == JsonValueKind.Object && details.TryGetProperty("family", out var f) ? f.GetString() : null));
        }
        return result;
    }

    public async Task<ModelEntry?> FindAsync(string model)
    {
        var name = WithTag(model);
        return (await ListAsync()).FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
    }

    public async Task<bool> ExistsAsync(string model) => await FindAsync(model) != null;

    public async Task<JsonObject> ShowAsync(string model)
    {
        using var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/show", new { model = WithTag(model) });
        resp.EnsureSuccessStatusCode();
        return JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsObject();
    }

    public async Task DeleteAsync(string model)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}/api/delete")
        {
            Content = JsonContent.Create(new { model = WithTag(model) })
        };
        using var resp = await Http.SendAsync(req);
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }

    /// <summary>Uploads a blob unless the server already has it.</summary>
    public async Task EnsureBlobAsync(string path, string digest)
    {
        using (var head = new HttpRequestMessage(HttpMethod.Head, $"{BaseUrl}/api/blobs/{digest}"))
        using (var headResp = await Http.SendAsync(head))
        {
            if (headResp.IsSuccessStatusCode) return;
        }

        await using var file = File.OpenRead(path);
        using var content = new StreamContent(file);
        using var resp = await Http.PostAsync($"{BaseUrl}/api/blobs/{digest}", content);
        resp.EnsureSuccessStatusCode();
    }

    /// <summary>Creates <paramref name="model"/> from the test base model (GGUF + Modelfile template/parameters).</summary>
    public async Task CreateFromTestModelAsync(string model, TestModelAsset asset)
    {
        await EnsureBlobAsync(asset.GgufPath, asset.Digest);
        var body = new Dictionary<string, object>
        {
            ["model"] = WithTag(model),
            ["files"] = new Dictionary<string, string> { [asset.FileName] = asset.Digest },
            ["template"] = asset.Template,
            ["parameters"] = asset.Parameters,
            ["stream"] = false
        };
        using var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/create", body);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode || text.Contains("\"error\""))
            throw new InvalidOperationException($"Creating test model {model} on {BaseUrl} failed: {(int)resp.StatusCode} {text}");
    }

    /// <summary>
    /// Pulls a model from the registry (used only by @registry tests). Server errors (the server could not reach
    /// the registry: connection reset, timeout) are retried, so a network hiccup does not fail the scenario's setup.
    /// </summary>
    public async Task PullAsync(string model)
    {
        const int attempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            using var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/pull", new { model = WithTag(model), stream = false });
            var text = await resp.Content.ReadAsStringAsync();
            if (resp.IsSuccessStatusCode && !text.Contains("\"error\"")) return;
            if ((int)resp.StatusCode < 500 || attempt == attempts)
                throw new InvalidOperationException($"Pulling {model} on {BaseUrl} failed: {(int)resp.StatusCode} {text}");
            await Task.Delay(TimeSpan.FromSeconds(2 << attempt));
        }
    }

    public async Task<List<string>> LoadedAsync()
    {
        using var doc = JsonDocument.Parse(await Http.GetStringAsync($"{BaseUrl}/api/ps"));
        return doc.RootElement.GetProperty("models").EnumerateArray()
            .Select(m => m.GetProperty("name").GetString()!)
            .ToList();
    }

    /// <summary>Loads a model into memory (empty generate request with keep_alive).</summary>
    public async Task LoadAsync(string model)
    {
        using var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/generate",
            new { model = WithTag(model), keep_alive = "10m", stream = false });
        resp.EnsureSuccessStatusCode();
    }

    public async Task UnloadAsync(string model)
    {
        using var resp = await Http.PostAsJsonAsync($"{BaseUrl}/api/generate",
            new { model = WithTag(model), keep_alive = 0, stream = false });
        if (resp.StatusCode != HttpStatusCode.NotFound)
            resp.EnsureSuccessStatusCode();
    }
}

public sealed record ModelEntry(string Name, long Size, string Digest, string? QuantizationLevel, string? Family);
