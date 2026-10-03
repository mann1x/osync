using System.Net.Http.Json;
using System.Text.Json;

namespace osync
{
    /// <summary>
    /// xOllama's media engines: a model can carry image generation/editing, speech, transcription or video engines
    /// (the "media" block of its xOllama settings, schema v7). A model with media and no LLM (a "media template") has
    /// no "completion" capability and empty details: it lists and copies like any model but cannot chat. The engines,
    /// their settings and their rules live in xOllama; osync only tells the kinds apart.
    /// </summary>
    internal static class XOllamaMedia
    {
        /// <summary>Media type of a media component layer (media/&lt;kind&gt;.&lt;role&gt;): a blob moved like weights.</summary>
        public const string Layer = "application/vnd.xollama.media";

        /// <summary>The kinds osync shows and filters on (<c>osync ls --kind</c>).</summary>
        public static readonly string[] Kinds = { "llm", "embed", "image", "stt", "tts", "video" };

        /// <summary>The kind a capability of /api/tags or /api/show stands for, or null when it names none.</summary>
        public static string? KindOf(string capability) => capability switch
        {
            "completion" or "decision" => "llm",
            "embedding" => "embed",
            "image_generation" or "image_edit" => "image",
            "transcription" => "stt",
            "speech" => "tts",
            "video" => "video",
            _ => null
        };

        /// <summary>Whether <paramref name="kind"/> is a media engine's kind.</summary>
        public static bool IsMediaKind(string kind) => kind is "image" or "stt" or "tts" or "video";

        /// <summary>The kinds of a capabilities list, in <see cref="Kinds"/> order and without repeats.</summary>
        public static List<string> KindsOf(IEnumerable<string> capabilities)
        {
            var kinds = capabilities.Select(KindOf).OfType<string>().ToHashSet();
            return Kinds.Where(kinds.Contains).ToList();
        }

        /// <summary>The media kinds stated in xOllama settings: the keys of their "media" block (image, stt, tts, video).</summary>
        public static List<string> KindsOfSettings(JsonElement settings)
        {
            if (settings.ValueKind != JsonValueKind.Object || !settings.TryGetProperty("media", out var media) ||
                media.ValueKind != JsonValueKind.Object)
                return new List<string>();
            var kinds = media.EnumerateObject()
                .Where(p => p.Value.ValueKind == JsonValueKind.Object)
                .Select(p => p.Name)
                .ToHashSet();
            return Kinds.Where(kinds.Contains).ToList();
        }

        /// <summary>Whether a model of these kinds has media engines and no LLM, so it cannot chat or generate text.</summary>
        public static bool IsMediaOnly(IReadOnlyCollection<string> kinds) =>
            kinds.Any(IsMediaKind) && !kinds.Contains("llm");

        /// <summary>The capabilities of an /api/tags row or an /api/show answer; empty when the server lists none.</summary>
        public static List<string> CapabilitiesOf(JsonElement model) =>
            model.ValueKind == JsonValueKind.Object && model.TryGetProperty("capabilities", out var caps) &&
            caps.ValueKind == JsonValueKind.Array
                ? caps.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
                : new List<string>();

        /// <summary>
        /// Why <paramref name="model"/> cannot chat, from its /api/show answer: it is a media model; null when it can
        /// (or the server does not say).
        /// </summary>
        public static string? CannotChatReason(string model, JsonElement show)
        {
            var kinds = KindsOf(CapabilitiesOf(show));
            return IsMediaOnly(kinds)
                ? $"'{model}' is a media model ({string.Join(", ", kinds)}) with no LLM: it does not chat or generate text"
                : null;
        }

        /// <summary>
        /// Asks <paramref name="server"/> for <paramref name="model"/>'s capabilities and returns
        /// <see cref="CannotChatReason"/>; null when it can chat or the server cannot tell.
        /// </summary>
        public static async Task<string?> CannotChatReasonAsync(HttpClient client, string server, string model)
        {
            try
            {
                using var response = await client.PostAsJsonAsync($"{server.TrimEnd('/')}/api/show", new { model });
                if (!response.IsSuccessStatusCode) return null;
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return CannotChatReason(model, doc.RootElement);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// Throws when <paramref name="response"/> failed, with the server's own error text ("the opencoti engine does
        /// not offer ..., update the engine"): it says what to do, so it is shown as is rather than as a status code.
        /// </summary>
        public static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode) return;
            var body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(ErrorText(body) is { Length: > 0 } error
                ? error
                : $"HTTP {(int)response.StatusCode} ({response.StatusCode})", null, response.StatusCode);
        }

        /// <summary>The "error" of an Ollama error answer, or the body itself when it is not one.</summary>
        public static string ErrorText(string body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    return e.GetString() ?? "";
            }
            catch (JsonException)
            {
                // not JSON
            }
            return body.Trim();
        }
    }
}
