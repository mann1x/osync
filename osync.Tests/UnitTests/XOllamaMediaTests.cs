using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace osync.Tests.UnitTests;

public class XOllamaMediaTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Theory]
    [InlineData(new[] { "speech" }, new[] { "tts" })]
    [InlineData(new[] { "transcription" }, new[] { "stt" })]
    [InlineData(new[] { "image_generation", "image_edit" }, new[] { "image" })]
    [InlineData(new[] { "video" }, new[] { "video" })]
    [InlineData(new[] { "completion", "tools", "vision", "speech" }, new[] { "llm", "tts" })]
    [InlineData(new[] { "decision" }, new[] { "llm" })]
    [InlineData(new[] { "embedding" }, new[] { "embed" })]
    [InlineData(new[] { "tools", "thinking" }, new string[0])]
    public void KindsOf_MapsCapabilities(string[] capabilities, string[] expected) =>
        XOllamaMedia.KindsOf(capabilities).Should().Equal(expected);

    [Theory]
    [InlineData(new[] { "tts" }, true)]
    [InlineData(new[] { "image", "stt" }, true)]
    [InlineData(new[] { "llm", "tts" }, false)]
    [InlineData(new[] { "llm" }, false)]
    [InlineData(new[] { "embed" }, false)]
    [InlineData(new string[0], false)]
    public void IsMediaOnly_MediaWithoutAnLlm(string[] kinds, bool expected) =>
        XOllamaMedia.IsMediaOnly(kinds).Should().Be(expected);

    [Fact]
    public void KindsOfSettings_ReadsTheMediaBlock()
    {
        var settings = Json("""{"version":7,"media":{"tts":{"engine":"outetts","model":"sha256:aa"},"image":{"model":"sha256:bb"}}}""");
        XOllamaMedia.KindsOfSettings(settings).Should().Equal("image", "tts");
        XOllamaMedia.KindsOfSettings(Json("""{"version":6,"kv":{"k":"q8_0"}}""")).Should().BeEmpty();
    }

    [Fact]
    public void CannotChatReason_OnlyForMediaOnlyModels()
    {
        XOllamaMedia.CannotChatReason("mannix/outetts:0.3", Json("""{"capabilities":["speech"],"details":{"format":""}}"""))
            .Should().Be("'mannix/outetts:0.3' is a media model (tts) with no LLM: it does not chat or generate text");
        XOllamaMedia.CannotChatReason("kit", Json("""{"capabilities":["completion","speech"]}""")).Should().BeNull();
        XOllamaMedia.CannotChatReason("old", Json("""{"details":{"format":"gguf"}}""")).Should().BeNull("an old server lists no capabilities");
    }

    [Fact]
    public async Task EnsureSuccess_ThrowsTheServersOwnError()
    {
        const string error = "the opencoti engine does not offer audio_speech_content_format_v1, which this model's media needs; update the engine";
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { error }))
        };

        var act = () => XOllamaMedia.EnsureSuccessAsync(response);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.Message.Should().Be(error);
    }

    [Theory]
    [InlineData("""{"error":"pull the model again"}""", "pull the model again")]
    [InlineData("plain text", "plain text")]
    [InlineData("", "")]
    public void ErrorText_TakesTheErrorField(string body, string expected) =>
        XOllamaMedia.ErrorText(body).Should().Be(expected);

    [Fact]
    public void RemoteModelKinds_FromCapabilities_ElseTheFormat()
    {
        OsyncProgram.RemoteModelKinds(new OllamaModel
        {
            capabilities = new List<string> { "image_generation", "image_edit" },
            details = new OllamaModelDetails { format = "" }
        }).Should().Equal("image");
        OsyncProgram.RemoteModelKinds(new OllamaModel { details = new OllamaModelDetails { format = "gguf" } })
            .Should().Equal("llm");
        OsyncProgram.RemoteModelKinds(new OllamaModel()).Should().BeEmpty();
    }
}
