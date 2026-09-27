using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class ModelRecreateTests
{
    private const string Weights = "sha256:1111111111111111111111111111111111111111111111111111111111111111";
    private const string Config = "sha256:2222222222222222222222222222222222222222222222222222222222222222";
    private const string Params = "sha256:3333333333333333333333333333333333333333333333333333333333333333";
    private const string XOllama = "sha256:4444444444444444444444444444444444444444444444444444444444444444";
    private const string Template = "sha256:5555555555555555555555555555555555555555555555555555555555555555";

    private static byte[] Manifest(params (string MediaType, string Digest)[] layers) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 2,
            config = new { mediaType = "application/vnd.docker.container.image.v1+json", digest = Config },
            layers = layers.Select(l => new { mediaType = l.MediaType, digest = l.Digest, size = 1 })
        });

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    // A council model as xOllama stores it: no template layer (the renderer formats the prompt)
    private static readonly byte[] CouncilManifest = Manifest(
        (ModelRecreate.ModelLayer, Weights), (ModelRecreate.XOllamaLayer, XOllama), (ModelRecreate.ParamsLayer, Params));

    private static readonly Dictionary<string, byte[]> CouncilBlobs = new()
    {
        [Config] = Encoding.UTF8.GetBytes("""{"model_format":"gguf","renderer":"qwen3.5","parser":"qwen3.5","requires":"0.30.0"}"""),
        [XOllama] = Encoding.UTF8.GetBytes("""{"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true}}"""),
        [Params] = Encoding.UTF8.GetBytes("""{"temperature":0.6,"stop":["<|im_end|>"]}""")
    };

    // What /api/show reports for it: note the placeholder template of a model that has none
    private static readonly JsonElement CouncilShow = Json("""
        {"template":"{{ .Prompt }}","parameters":"temperature 0.6\nstop \"<|im_end|>\"",
         "renderer":"qwen3.5","parser":"qwen3.5","requires":"0.30.0",
         "xollama":{"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true}}}
        """);

    [Fact]
    public void BuildCreateRequest_TakesEveryPartFromTheBlobs()
    {
        var request = ModelRecreate.BuildCreateRequest("omni-council:latest", CouncilManifest,
            d => CouncilBlobs.GetValueOrDefault(d), CouncilShow);

        request["files"].Should().BeEquivalentTo(new Dictionary<string, string> { ["model.gguf"] = Weights });
        request["renderer"].Should().Be("qwen3.5");
        request["parser"].Should().Be("qwen3.5");
        request["requires"].Should().Be("0.30.0");
        ((JsonElement)request["xollama"]).GetRawText().Should().Be("""{"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true}}""");
        ((JsonElement)request["parameters"]).GetRawText().Should().Be("""{"temperature":0.6,"stop":["<|im_end|>"]}""");
        request.Should().NotContainKey("template", "the model has no template layer");
    }

    [Fact]
    public void BuildCreateRequest_FallsBackToShowForBlobsItDoesNotHave()
    {
        var request = ModelRecreate.BuildCreateRequest("omni-council:latest", CouncilManifest, _ => null, CouncilShow);

        request["renderer"].Should().Be("qwen3.5");
        request["parser"].Should().Be("qwen3.5");
        request["requires"].Should().Be("0.30.0");
        ((JsonElement)request["xollama"]).GetProperty("council").GetProperty("enabled").GetBoolean().Should().BeTrue();
        request["parameters"].Should().BeEquivalentTo(new Dictionary<string, object>
        {
            ["temperature"] = 0.6,
            ["stop"] = new List<object> { "<|im_end|>" }
        });
        request.Should().NotContainKey("template", "show's placeholder is not a template layer");
    }

    [Fact]
    public void BuildCreateRequest_KeepsATemplateLayerAndNamesFilesByKind()
    {
        const string projector = "sha256:6666666666666666666666666666666666666666666666666666666666666666";
        const string split = "sha256:7777777777777777777777777777777777777777777777777777777777777777";
        var manifest = Manifest((ModelRecreate.ModelLayer, Weights), (ModelRecreate.ModelLayer, split),
            (ModelRecreate.ProjectorLayer, projector), (ModelRecreate.TemplateLayer, Template));

        var request = ModelRecreate.BuildCreateRequest("m:latest", manifest,
            d => d == Template ? Encoding.UTF8.GetBytes("{{ .System }} {{ .Prompt }}") : null, show: null);

        request["template"].Should().Be("{{ .System }} {{ .Prompt }}");
        request["files"].Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["model.gguf"] = Weights, ["model_1.gguf"] = split, ["projector.gguf"] = projector
        });
        request.Should().NotContainKeys("renderer", "parser", "requires");
    }

    [Fact]
    public void BuildCreateRequest_RefusesLayersItCannotRebuild()
    {
        var manifest = Manifest(("application/vnd.ollama.image.tensor", Weights));

        var act = () => ModelRecreate.BuildCreateRequest("m:latest", manifest, _ => null, show: null);

        act.Should().Throw<NotSupportedException>().WithMessage("*vnd.ollama.image.tensor*");
    }

    private static JsonElement ShowWithModelfile(string modelfile, string extra = "") =>
        Json($$"""{"modelfile":{{JsonSerializer.Serialize(modelfile)}}{{extra}}}""");

    private const string SourceModelfile = """
        # Modelfile generated by "xollama show"
        # To build a new Modelfile based on this, replace FROM with:
        # FROM omni-council:latest

        FROM /srv/ml/models/blobs/sha256-918202d6bd81
        RENDERER qwen3.5
        PARSER qwen3.5
        PARAMETER temperature 0.6
        PARAMETER stop <|im_end|>
        LICENSE MIT
        LICENSE Apache
        XOLLAMA {"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true}}
        """;

    [Fact]
    public void CompareShow_MatchingCopyHasNoDifferences()
    {
        // Another server: its own models directory and header, parameters in another order
        var copy = SourceModelfile
            .Replace("PARAMETER temperature 0.6\nPARAMETER stop <|im_end|>", "PARAMETER stop <|im_end|>\nPARAMETER temperature 0.6")
            .Replace("/srv/ml/models/blobs/", "C:\\Users\\me\\.ollama\\models\\blobs\\")
            .Replace("omni-council:latest", "copy:latest");

        var (lost, derived) = ModelRecreate.CompareShow(ShowWithModelfile(SourceModelfile), ShowWithModelfile(copy), destIsXOllama: true);

        lost.Should().BeEmpty();
        derived.Should().BeEmpty();
    }

    [Fact]
    public void CompareShow_ReportsWhatTheOldRecreateLost()
    {
        // What osync 1.4.0's fallback produced: a placeholder template, no renderer/parser, one license, no settings
        var copy = """
            FROM /srv/ml/models/blobs/sha256-918202d6bd81
            TEMPLATE {{ .Prompt }}
            PARAMETER temperature 0.6
            PARAMETER stop <|im_end|>
            LICENSE MIT
            Apache
            """;

        var (lost, _) = ModelRecreate.CompareShow(ShowWithModelfile(SourceModelfile), ShowWithModelfile(copy), destIsXOllama: true);

        lost.Should().Equal("LICENSE", "PARSER", "RENDERER", "TEMPLATE", "XOLLAMA");
    }

    [Fact]
    public void CompareShow_IgnoresXOllamaSettingsOnOllama()
    {
        var copy = SourceModelfile.Replace("""XOLLAMA {"version":4,"kv":{"k":"f16","v":"q8_0"},"council":{"enabled":true}}""", "");

        ModelRecreate.CompareShow(ShowWithModelfile(SourceModelfile), ShowWithModelfile(copy), destIsXOllama: false)
            .Lost.Should().BeEmpty();
    }

    [Fact]
    public void CompareShow_ReportsDerivedDifferencesSeparately()
    {
        var source = ShowWithModelfile(SourceModelfile, ""","capabilities":["completion","tools"],"details":{"format":"gguf","family":"qwen3"}""");
        var copy = ShowWithModelfile(SourceModelfile, ""","capabilities":["tools"],"details":{"format":"gguf","family":"qwen3"}""");

        var (lost, derived) = ModelRecreate.CompareShow(source, copy, destIsXOllama: false);

        lost.Should().BeEmpty();
        derived.Should().Equal("capabilities");
    }

    [Fact]
    public void ModelfileEntries_KeepsMultiLineInstructionsTogether()
    {
        var entries = ModelRecreate.ModelfileEntries("""
            FROM /models/blobs/sha256-abc
            TEMPLATE "{{- range .Messages }}
            {{ .Content }}
            {{ end }}"
            SYSTEM be nice
            """);

        entries["FROM"].Should().Equal("sha256-abc");
        entries["TEMPLATE"].Should().Equal("\"{{- range .Messages }}\n{{ .Content }}\n{{ end }}\"");
        entries["SYSTEM"].Should().Equal("be nice");
    }

    [Fact]
    public void InlineBlobs_ListsTheConfigAndSmallSettingsLayers()
    {
        ModelRecreate.InlineBlobs(CouncilManifest, 1024).Should().Equal(Config, XOllama, Params);
    }

    [Theory]
    [InlineData("/api/pull on http://192.168.178.161:22434 failed: 500 mkdir C:\\Users\\me\\.ollama\\models\\manifests\\192.168.178.2:32025: The directory name is invalid.", "192.168.178.2:32025", true)]
    [InlineData("/api/copy on http://win:11434 failed: 500 mkdir C:\\Users\\me\\.ollama\\models\\manifests\\10.0.0.2:41234: The directory name is invalid.", "10.0.0.2:41234", true)]
    [InlineData("/api/pull on http://gpu:11434 failed: 500 dial tcp 192.168.178.2:32025: connect: connection refused", "192.168.178.2:32025", false)]
    [InlineData("/api/pull failed: 500 mkdir /models/manifests/192.168.178.2: permission denied", "192.168.178.2", false)]
    public void IsWindowsManifestPathError_RecognisesTheColonInTheManifestPath(string message, string authority, bool expected)
    {
        RelayCopy.IsWindowsManifestPathError(message, authority).Should().Be(expected);
    }
}
