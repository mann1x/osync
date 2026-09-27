using System.Text;
using System.Text.Json;

namespace osync
{
    /// <summary>
    /// Rebuilds a model on another server with /api/create from its manifest, and checks that the result matches the
    /// source. The manifest decides which parts exist; each part's content comes from its blob when osync has it (the
    /// local models directory, or a small blob the relay saw) and otherwise from the source's /api/show.
    /// /api/show alone is not enough: it reports a default template for a model that has none, and a created copy
    /// would then carry that placeholder as a real template layer.
    /// </summary>
    internal static class ModelRecreate
    {
        public const string ModelLayer = "application/vnd.ollama.image.model";
        public const string ProjectorLayer = "application/vnd.ollama.image.projector";
        public const string AdapterLayer = "application/vnd.ollama.image.adapter";
        public const string DraftLayer = "application/vnd.ollama.image.draft";
        public const string TemplateLayer = "application/vnd.ollama.image.template";
        public const string SystemLayer = "application/vnd.ollama.image.system";
        public const string LicenseLayer = "application/vnd.ollama.image.license";
        public const string MessagesLayer = "application/vnd.ollama.image.messages";
        public const string ParamsLayer = "application/vnd.ollama.image.params";
        /// <summary>xOllama's per-model settings (council, KV cache types, ...): the "xollama" field of /api/create.</summary>
        public const string XOllamaLayer = "application/vnd.ollama.image.json";

        /// <summary>Layers whose blob must be on the destination before /api/create (sent by digest).</summary>
        public static bool IsFileLayer(string mediaType) =>
            mediaType is ModelLayer or ProjectorLayer or AdapterLayer or DraftLayer;

        /// <summary>
        /// Digests of the blobs <see cref="BuildCreateRequest"/> sends inline: the config and every layer that is not a
        /// file (template, system, license, messages, parameters, xOllama settings), up to <paramref name="maxSize"/>.
        /// </summary>
        public static List<string> InlineBlobs(byte[] manifest, long maxSize)
        {
            using var doc = JsonDocument.Parse(manifest);
            var root = doc.RootElement;
            var digests = new List<string>();
            if (root.TryGetProperty("config", out var config) && config.TryGetProperty("digest", out var configDigest))
                digests.Add(configDigest.GetString()!);
            foreach (var layer in root.GetProperty("layers").EnumerateArray())
            {
                var size = layer.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
                if (!IsFileLayer(layer.GetProperty("mediaType").GetString() ?? "") && size <= maxSize)
                    digests.Add(layer.GetProperty("digest").GetString()!);
            }
            return digests;
        }

        /// <summary>
        /// The /api/create body that recreates the model described by <paramref name="manifest"/>.
        /// <paramref name="readBlob"/> returns a blob's bytes, or null when they are not available here;
        /// <paramref name="show"/> is the source's /api/show answer, used for the parts whose blob is not available.
        /// Throws <see cref="NotSupportedException"/> for layers /api/create cannot rebuild (safetensors tensors, ...).
        /// </summary>
        public static Dictionary<string, object> BuildCreateRequest(string destModel, byte[] manifest,
            Func<string, byte[]?> readBlob, JsonElement? show)
        {
            using var doc = JsonDocument.Parse(manifest);
            var root = doc.RootElement;

            var files = new Dictionary<string, string>();
            var adapters = new Dictionary<string, string>();
            var draftFiles = new Dictionary<string, string>();
            var request = new Dictionary<string, object> { ["model"] = destModel, ["stream"] = false };
            var licenses = new List<string>();
            int models = 0, projectors = 0;

            foreach (var layer in root.GetProperty("layers").EnumerateArray())
            {
                var mediaType = layer.GetProperty("mediaType").GetString() ?? "";
                var digest = layer.GetProperty("digest").GetString() ?? "";
                switch (mediaType)
                {
                    case ModelLayer:
                        files[models == 0 ? "model.gguf" : $"model_{models}.gguf"] = digest;
                        models++;
                        break;
                    case ProjectorLayer:
                        files[projectors == 0 ? "projector.gguf" : $"projector_{projectors}.gguf"] = digest;
                        projectors++;
                        break;
                    case AdapterLayer:
                        adapters[adapters.Count == 0 ? "adapter.gguf" : $"adapter_{adapters.Count}.gguf"] = digest;
                        break;
                    case DraftLayer:
                        draftFiles[draftFiles.Count == 0 ? "draft.gguf" : $"draft_{draftFiles.Count}.gguf"] = digest;
                        break;
                    case TemplateLayer:
                        request["template"] = TextOf(readBlob(digest), show, "template");
                        break;
                    case SystemLayer:
                        request["system"] = TextOf(readBlob(digest), show, "system");
                        break;
                    case LicenseLayer:
                        licenses.Add(digest);
                        break;
                    case MessagesLayer:
                        request["messages"] = JsonOf(readBlob(digest), show, "messages");
                        break;
                    case ParamsLayer:
                        if (readBlob(digest) is { } parameters)
                            request["parameters"] = ParseJson(parameters);
                        else if (show?.TryGetProperty("parameters", out var text) == true && text.ValueKind == JsonValueKind.String)
                            request["parameters"] = RelayCopy.ParseParameters(text.GetString()!);
                        break;
                    case XOllamaLayer:
                        request["xollama"] = JsonOf(readBlob(digest), show, "xollama");
                        break;
                    default:
                        throw new NotSupportedException($"the model has a {mediaType} layer, which /api/create cannot rebuild");
                }
            }

            if (files.Count == 0)
                throw new NotSupportedException("the model has no GGUF layers that /api/create can use");
            request["files"] = files;
            if (adapters.Count > 0) request["adapters"] = adapters;
            if (draftFiles.Count > 0) request["draft_files"] = draftFiles;
            if (licenses.Count > 0)
            {
                // One layer per license; /api/show joins them, so it is only the source when a blob is missing
                var texts = licenses.Select(readBlob).ToList();
                request["license"] = texts.All(t => t != null)
                    ? texts.Count == 1 ? Encoding.UTF8.GetString(texts[0]!) : texts.Select(t => Encoding.UTF8.GetString(t!)).ToList()
                    : ShowString(show, "license");
            }

            // renderer, parser and requires live in the config blob, not in a layer
            JsonElement? config = null;
            using var configDoc = root.TryGetProperty("config", out var configRef) &&
                                  configRef.TryGetProperty("digest", out var configDigest) &&
                                  readBlob(configDigest.GetString() ?? "") is { } configBytes
                ? TryParse(configBytes)
                : null;
            if (configDoc != null) config = configDoc.RootElement;
            foreach (var field in new[] { "renderer", "parser", "requires" })
            {
                var value = config != null ? StringField(config.Value, field) : StringField(show, field);
                if (!string.IsNullOrEmpty(value)) request[field] = value;
            }

            return request;
        }

        /// <summary>
        /// Compares the source's and the copy's /api/show answers. <c>Lost</c> lists the modelfile instructions that
        /// differ (TEMPLATE, SYSTEM, RENDERER, PARSER, PARAMETER, LICENSE, MESSAGE, XOLLAMA, ...): the copy is not the
        /// same model. The modelfile is compared rather than show's fields because show does not report every part as a
        /// field (renderer and parser, several licenses). <c>Derived</c> lists what the server works out from those parts
        /// (capabilities, format details), which can also differ between server versions.
        /// A destination that is not xOllama does not report xOllama settings, so they are compared only on xOllama.
        /// </summary>
        public static (List<string> Lost, List<string> Derived) CompareShow(JsonElement source, JsonElement dest, bool destIsXOllama)
        {
            var sourceEntries = ModelfileEntries(StringField(source, "modelfile") ?? "", destIsXOllama);
            var destEntries = ModelfileEntries(StringField(dest, "modelfile") ?? "", destIsXOllama);
            var lost = sourceEntries.Keys.Union(destEntries.Keys)
                .Where(k => !sourceEntries.TryGetValue(k, out var a) || !destEntries.TryGetValue(k, out var b) || !a.SequenceEqual(b))
                .Order(StringComparer.Ordinal)
                .ToList();

            var derived = new List<string>();
            if (!SameSet(Field(source, "capabilities"), Field(dest, "capabilities"))) derived.Add("capabilities");
            var sourceDetails = Field(source, "details");
            var destDetails = Field(dest, "details");
            foreach (var field in new[] { "format", "family", "parameter_size", "quantization_level" })
                if (StringField(sourceDetails, field) != StringField(destDetails, field))
                    derived.Add($"details.{field}");

            return (lost, derived);
        }

        private static readonly string[] ModelfileKeywords =
            { "FROM", "ADAPTER", "DRAFT", "TEMPLATE", "SYSTEM", "RENDERER", "PARSER", "REQUIRES", "PARAMETER", "LICENSE", "MESSAGE", "XOLLAMA" };

        /// <summary>
        /// The instructions of a modelfile from /api/show, grouped by keyword, in order. Comments are dropped, and the
        /// paths of FROM/ADAPTER/DRAFT are reduced to the blob name (each server has its own models directory).
        /// An instruction continues on the following lines until a line starts with another keyword. PARAMETER lines are
        /// sorted by name.
        /// </summary>
        internal static Dictionary<string, List<string>> ModelfileEntries(string modelfile, bool includeXOllama = true)
        {
            var entries = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            List<string>? current = null;
            var text = new StringBuilder();

            void Flush()
            {
                if (current != null) current.Add(text.ToString().TrimEnd('\n'));
                current = null;
                text.Clear();
            }

            foreach (var line in modelfile.Replace("\r\n", "\n").Split('\n'))
            {
                var keyword = ModelfileKeywords.FirstOrDefault(k => line.StartsWith(k + " ", StringComparison.Ordinal));
                if (keyword != null)
                {
                    Flush();
                    if (keyword == "XOLLAMA" && !includeXOllama) continue;
                    var value = line[(keyword.Length + 1)..];
                    if (keyword is "FROM" or "ADAPTER" or "DRAFT")
                        value = value.Trim().Replace('\\', '/').Split('/')[^1];
                    if (!entries.TryGetValue(keyword, out current)) entries[keyword] = current = new List<string>();
                    text.Append(value);
                }
                else if (current != null)
                {
                    text.Append('\n').Append(line);
                }
                // Lines before the first instruction are the generated comment header
            }
            Flush();

            // The server prints parameters from a map, in no fixed order; the values of one name keep their order
            if (entries.TryGetValue("PARAMETER", out var parameters))
                entries["PARAMETER"] = parameters.OrderBy(p => p.Split(' ', 2)[0], StringComparer.Ordinal).ToList();
            return entries;
        }

        private static string TextOf(byte[]? blob, JsonElement? show, string field) =>
            blob != null ? Encoding.UTF8.GetString(blob) : ShowString(show, field);

        private static string ShowString(JsonElement? show, string field) =>
            StringField(show, field) ?? throw new InvalidOperationException($"the source did not report the model's {field}");

        private static JsonElement JsonOf(byte[]? blob, JsonElement? show, string field)
        {
            if (blob != null) return ParseJson(blob);
            return Field(show, field) is { } value && value.ValueKind != JsonValueKind.Null
                ? value.Clone()
                : throw new InvalidOperationException($"the source did not report the model's {field}");
        }

        private static JsonElement ParseJson(byte[] blob)
        {
            using var doc = JsonDocument.Parse(blob);
            return doc.RootElement.Clone();
        }

        private static JsonDocument? TryParse(byte[] bytes)
        {
            try { return JsonDocument.Parse(bytes); }
            catch (JsonException) { return null; }
        }

        private static JsonElement? Field(JsonElement? element, string name) =>
            element is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty(name, out var value) ? value : null;

        private static string? StringField(JsonElement? element, string name) =>
            Field(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        private static bool SameSet(JsonElement? a, JsonElement? b)
        {
            static HashSet<string> Items(JsonElement? e) =>
                e is { ValueKind: JsonValueKind.Array } array
                    ? array.EnumerateArray().Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>();
            return Items(a).SetEquals(Items(b));
        }
    }
}
