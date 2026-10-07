using System.Text;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public sealed class ModelStoreTests : IDisposable
{
    private const string Child = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Converted = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "osync-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, true);
    }

    private string Write(string relative, string content)
    {
        var path = Path.Combine(_dir, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("llama3", "registry.ollama.ai", "library", "llama3", "latest")]
    [InlineData("llama3:8b", "registry.ollama.ai", "library", "llama3", "8b")]
    [InlineData("user/model:q4", "registry.ollama.ai", "user", "model", "q4")]
    [InlineData("hf.co/bartowski/SmolLM2-GGUF:Q2_K", "hf.co", "bartowski", "SmolLM2-GGUF", "Q2_K")]
    [InlineData("10.0.0.1:5000/ns/model", "10.0.0.1:5000", "ns", "model", "latest")]
    public void Parse_FillsTheDefaultParts(string name, string host, string ns, string model, string tag)
    {
        ModelStore.Parse(name).Should().Be(new ModelStore.ModelName(host, ns, model, tag));
    }

    [Theory]
    [InlineData("a/b/c/d")]
    [InlineData("../x")]
    [InlineData("model:")]
    public void Parse_RefusesNamesThatAreNotModels(string name)
    {
        ModelStore.Parse(name).Should().BeNull();
    }

    [Fact]
    public void ManifestCandidates_PutTheV2EntryFirst()
    {
        var candidates = ModelStore.ManifestCandidates(_dir, "llama3:8b");
        candidates[0].Should().Be(Path.Combine(_dir, "manifests-v2", "ollama.com", "library", "llama3", "8b"));
        candidates.Should().Contain(Path.Combine(_dir, "manifests", "registry.ollama.ai", "library", "llama3", "8b"));
    }

    [Fact]
    public void ManifestCandidates_EncodeAPortInTheV2Host()
    {
        ModelStore.ManifestCandidates(_dir, "10.0.0.1:5000/ns/model")[0]
            .Should().Be(Path.Combine(_dir, "manifests-v2", "10.0.0.1%3A5000", "ns", "model", "latest"));
    }

    [Fact]
    public void FindManifest_ReadsALegacyStore()
    {
        var path = Write("manifests/registry.ollama.ai/library/old/latest", "{}");
        ModelStore.FindManifest(_dir, "old").Should().Be(path);
    }

    [Fact]
    public void FindManifest_PrefersTheV2EntryOverTheDowngradeAnchor()
    {
        Write("manifests/registry.ollama.ai/library/m/latest", "{}");
        var v2 = Write("manifests-v2/ollama.com/library/m/latest", "{}");
        ModelStore.FindManifest(_dir, "m:latest").Should().Be(v2);
    }

    [Fact]
    public void FindManifest_ReturnsNullForAMissingModel()
    {
        ModelStore.FindManifest(_dir, "nothing").Should().BeNull();
    }

    [Fact]
    public void EnumerateManifests_ListsBothLayoutsOnce()
    {
        Write("manifests-v2/ollama.com/library/m/latest", "{}");
        Write("manifests/registry.ollama.ai/library/m/latest", "{}");
        Write("manifests/registry.ollama.ai/library/old/7b", "{}");
        Write("manifests-v2/ollama.com/user/x/q4", "{}");
        Write("manifests-v2/hf.co/bartowski/g/Q2_K", "{}");
        Write("manifests-v2/10.0.0.1%3A5000/ns/r/latest", "{}");
        Write("manifests-v2/ollama.com/library/m/.manifest-123", "{}");
        Write($"manifests/registry.ollama.ai/library/llamacpp/{Converted[7..]}", "{}");

        ModelStore.EnumerateManifests(_dir).Select(m => m.Name.Display).Should().BeEquivalentTo(
            "m:latest", "old:7b", "user/x:q4", "hf.co/bartowski/g:Q2_K", "10.0.0.1:5000/ns/r:latest");
    }

    private static string List(params (string Digest, string Runner)[] children) =>
        new JsonObject
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = ModelStore.ManifestListMediaType,
            ["manifests"] = new JsonArray(children.Select(c => (JsonNode)new JsonObject
            {
                ["mediaType"] = "application/vnd.docker.distribution.manifest.v2+json",
                ["digest"] = c.Digest,
                ["runner"] = c.Runner,
            }).ToArray())
        }.ToJsonString();

    [Fact]
    public void ReadModelManifest_ReturnsAPlainManifestAsIs()
    {
        var path = Write("manifests-v2/ollama.com/library/m/latest", "{\"schemaVersion\":2,\"layers\":[]}");
        ModelStore.ReadModelManifest(_dir, path, out var runner).Should().Equal(File.ReadAllBytes(path));
        runner.Should().BeNull();
    }

    [Fact]
    public void ReadModelManifest_TakesTheGgmlChildOfAList()
    {
        var path = Write("manifests-v2/ollama.com/library/m/latest", List((Converted, "llamacpp"), (Child, "ggml")));
        Write($"blobs/sha256-{Child[7..]}", "{\"child\":\"ggml\"}");
        Write($"blobs/sha256-{Converted[7..]}", "{\"child\":\"llamacpp\"}");

        Encoding.UTF8.GetString(ModelStore.ReadModelManifest(_dir, path, out var runner)).Should().Be("{\"child\":\"ggml\"}");
        runner.Should().Be("ggml");
    }

    [Fact]
    public void ReadModelManifest_FailsWhenTheChildIsNotLocal()
    {
        var path = Write("manifests-v2/ollama.com/library/m/latest", List((Child, "mlx")));
        var read = () => ModelStore.ReadModelManifest(_dir, path, out _);
        read.Should().Throw<InvalidOperationException>().WithMessage("*mlx*");
    }

    [Fact]
    public void SelectChild_FallsBackToLlamaCppThenTheFirst()
    {
        ModelStore.SelectChild((JsonObject)JsonNode.Parse(List((Child, "mlx"), (Converted, "llamacpp")))!)!.Value.Runner.Should().Be("llamacpp");
        ModelStore.SelectChild((JsonObject)JsonNode.Parse(List((Child, "mlx")))!)!.Value.Digest.Should().Be(Child);
    }
}
