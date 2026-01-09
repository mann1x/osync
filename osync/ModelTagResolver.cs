using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace osync;

/// <summary>
/// Resolves model tags from HuggingFace and Ollama registries.
/// Supports wildcard patterns for tag matching.
/// </summary>
public partial class ModelTagResolver
{
    private readonly HttpClient _httpClient;

    public ModelTagResolver(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "osync");
        }
    }

    /// <summary>
    /// Resolves a tag pattern to a list of matching tags.
    /// Supports wildcards (*) for pattern matching.
    /// </summary>
    /// <param name="modelSource">The model source (e.g., "hf.co/namespace/repo" or "llama3.2")</param>
    /// <param name="tagPattern">Tag pattern, may include wildcards (e.g., "Q4*", "IQ*", "*")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of resolved tags</returns>
    public async Task<List<ResolvedTag>> ResolveTagPatternAsync(
        string modelSource,
        string tagPattern,
        CancellationToken cancellationToken = default)
    {
        // If no wildcard, return the pattern as-is (it's a literal tag)
        if (!tagPattern.Contains('*'))
        {
            return new List<ResolvedTag> { new ResolvedTag(tagPattern, modelSource, null) };
        }

        // Fetch all available tags based on model source
        List<ResolvedTag> allTags;

        if (modelSource.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
        {
            allTags = await GetHuggingFaceTagsAsync(modelSource, cancellationToken);
        }
        else
        {
            allTags = await GetOllamaTagsAsync(modelSource, cancellationToken);
        }

        // Filter tags by wildcard pattern (case-insensitive)
        var pattern = WildcardToRegex(tagPattern);
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);

        return allTags.Where(t => regex.IsMatch(t.Tag)).ToList();
    }

    /// <summary>
    /// Fetches available GGUF quantization tags from HuggingFace.
    /// </summary>
    public async Task<List<ResolvedTag>> GetHuggingFaceTagsAsync(
        string modelSource,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ResolvedTag>();

        try
        {
            // Parse hf.co/{namespace}/{repo}
            var path = modelSource;
            if (path.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring("hf.co/".Length);
            }

            var url = $"https://huggingface.co/api/models/{path}";
            var modelInfo = await _httpClient.GetFromJsonAsync<HfModelInfo>(url, cancellationToken);

            if (modelInfo?.Siblings == null)
                return result;

            foreach (var sibling in modelInfo.Siblings)
            {
                if (string.IsNullOrEmpty(sibling.Filename))
                    continue;

                if (!sibling.Filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                    continue;

                var quantTag = ExtractQuantTag(sibling.Filename);
                if (quantTag != null)
                {
                    result.Add(new ResolvedTag(quantTag, modelSource, sibling.Filename));
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching HuggingFace tags: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Fetches available tags from Ollama registry by scraping the tags page.
    /// Note: Ollama's registry does NOT expose a standard OCI /tags/list endpoint.
    /// </summary>
    public async Task<List<ResolvedTag>> GetOllamaTagsAsync(
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ResolvedTag>();

        try
        {
            // Handle namespaced models (e.g., "mannix/gemma3")
            var urlModelName = modelName.Contains('/') ? modelName : modelName;
            var url = $"https://ollama.com/library/{urlModelName}/tags";

            var html = await _httpClient.GetStringAsync(url, cancellationToken);
            var tags = ParseOllamaTagsFromHtml(html, modelName);

            foreach (var tag in tags)
            {
                result.Add(new ResolvedTag(tag.Tag, modelName, null, tag.Size, tag.Digest));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error fetching Ollama tags: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Extracts the quantization tag from a GGUF filename.
    /// Examples:
    ///   "LFM2.5-1.2B-Instruct-Q4_K_M.gguf" -> "Q4_K_M"
    ///   "model-F16.gguf" -> "F16"
    ///   "model-BF16.gguf" -> "BF16"
    /// </summary>
    public static string? ExtractQuantTag(string filename)
    {
        // Remove .gguf extension
        if (!filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return null;

        var baseName = filename[..^5];

        // Common quantization patterns
        var match = QuantizationPattern().Match(baseName);

        return match.Success ? match.Value : null;
    }

    /// <summary>
    /// Parses Ollama model tags from the HTML tags page.
    /// </summary>
    private static List<OllamaModelTag> ParseOllamaTagsFromHtml(string html, string modelName)
    {
        var result = new List<OllamaModelTag>();

        // Pattern to match tag links like: /library/llama3.2:1b-instruct-q4_K_M
        var tagLinkPattern = TagLinkPattern();

        // Pattern to extract tag details (digest • size • context)
        var detailsPattern = TagDetailsPattern();

        var tagMatches = tagLinkPattern.Matches(html);

        foreach (Match match in tagMatches)
        {
            var fullTag = match.Groups[1].Value; // e.g., "llama3.2:1b-instruct-q4_K_M"
            var tagPart = fullTag.Contains(':')
                ? fullTag.Substring(fullTag.IndexOf(':') + 1)
                : "latest";

            // Find the details section following this tag
            var startIndex = match.Index;
            var searchArea = html.Substring(startIndex, Math.Min(500, html.Length - startIndex));

            var detailsMatch = detailsPattern.Match(searchArea);

            string? digest = null;
            string? size = null;
            string? context = null;

            if (detailsMatch.Success)
            {
                digest = detailsMatch.Groups[1].Value;
                size = detailsMatch.Groups[2].Value;
                context = detailsMatch.Groups[3].Value;
            }

            // Avoid duplicates
            if (!result.Any(t => t.Tag == tagPart))
            {
                result.Add(new OllamaModelTag(tagPart, digest, size, context));
            }
        }

        return result;
    }

    /// <summary>
    /// Converts a wildcard pattern to a regex pattern.
    /// * matches any characters, ? matches single character.
    /// </summary>
    private static string WildcardToRegex(string pattern)
    {
        // Escape regex special characters except * and ?
        var escaped = Regex.Escape(pattern);
        // Replace escaped wildcards with regex equivalents
        escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
        // Anchor to match entire string
        return $"^{escaped}$";
    }

    // Regex pattern for common GGUF quantization suffixes
    // Matches: Q4_0, Q4_K_M, Q5_K_S, Q8_0, IQ2_XXS, IQ3_M, IQ4_NL, F16, F32, BF16
    [GeneratedRegex(@"(?:IQ[1-4]_(?:XXS|XS|S|M|NL)|Q[2-8]_(?:K_[SML]|K|[01])|[FB]F?(?:16|32))$",
        RegexOptions.IgnoreCase)]
    private static partial Regex QuantizationPattern();

    // Matches: /library/modelname:tag
    [GeneratedRegex(@"/library/([^""'\s\]\)]+:[^""'\s\]\)]+)")]
    private static partial Regex TagLinkPattern();

    // Matches: digest • size • context
    [GeneratedRegex(@"([a-f0-9]{12})\s*•\s*([\d.]+[GMKB]+)\s*•\s*(\d+K)\s*context")]
    private static partial Regex TagDetailsPattern();
}

/// <summary>
/// Represents a resolved model tag with metadata.
/// </summary>
public record ResolvedTag(
    string Tag,
    string ModelSource,
    string? Filename = null,
    string? Size = null,
    string? Digest = null)
{
    /// <summary>
    /// Gets the full model name for use with Ollama (e.g., "hf.co/namespace/repo:tag" or "model:tag").
    /// </summary>
    public string GetFullModelName()
    {
        if (ModelSource.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{ModelSource}:{Tag}";
        }
        return $"{ModelSource}:{Tag}";
    }
}

/// <summary>
/// Represents an Ollama model tag with its metadata.
/// </summary>
public record OllamaModelTag(
    string Tag,
    string? Digest = null,
    string? Size = null,
    string? ContextWindow = null);

#region HuggingFace API Models

public class HfModelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("modelId")]
    public string? ModelId { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("siblings")]
    public List<HfSibling>? Siblings { get; set; }

    [JsonPropertyName("gguf")]
    public HfGgufInfo? Gguf { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
}

public class HfSibling
{
    [JsonPropertyName("rfilename")]
    public string? Filename { get; set; }
}

public class HfGgufInfo
{
    [JsonPropertyName("total")]
    public long? Total { get; set; }

    [JsonPropertyName("architecture")]
    public string? Architecture { get; set; }

    [JsonPropertyName("context_length")]
    public int? ContextLength { get; set; }
}

#endregion
