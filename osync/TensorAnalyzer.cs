using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace osync;

/// <summary>
/// Analyzes model tensor quantization distribution.
/// </summary>
public partial class TensorAnalyzer
{
    private readonly HttpClient _httpClient;

    // Bits per weight for different quantization types (approximate)
    private static readonly Dictionary<string, double> QuantizationBitsPerWeight = new(StringComparer.OrdinalIgnoreCase)
    {
        // Standard quantizations
        { "Q2_K", 2.5 },
        { "Q2_K_S", 2.5 },
        { "Q2_K_M", 2.5 },
        { "Q2_K_L", 2.5 },
        { "Q2_K_XL", 2.5 },
        { "Q3_K", 3.5 },
        { "Q3_K_S", 3.5 },
        { "Q3_K_M", 3.5 },
        { "Q3_K_L", 3.5 },
        { "Q3_K_XL", 3.5 },
        { "Q4_0", 4.5 },
        { "Q4_1", 5.0 },
        { "Q4_K", 4.5 },
        { "Q4_K_S", 4.5 },
        { "Q4_K_M", 4.5 },
        { "Q4_K_L", 4.5 },
        { "Q4_K_XL", 4.5 },
        { "Q5_0", 5.5 },
        { "Q5_1", 6.0 },
        { "Q5_K", 5.5 },
        { "Q5_K_S", 5.5 },
        { "Q5_K_M", 5.5 },
        { "Q5_K_L", 5.5 },
        { "Q5_K_XL", 5.5 },
        { "Q6_K", 6.5 },
        { "Q6_K_S", 6.5 },
        { "Q6_K_M", 6.5 },
        { "Q6_K_L", 6.5 },
        { "Q6_K_XL", 6.5 },
        { "Q8_0", 8.5 },
        { "Q8_K", 8.5 },
        { "Q8_K_S", 8.5 },
        { "Q8_K_M", 8.5 },
        { "Q8_K_L", 8.5 },
        { "Q8_K_XL", 8.5 },

        // IQ quantizations (importance matrix)
        { "IQ1_S", 1.5 },
        { "IQ1_M", 1.75 },
        { "IQ2_XXS", 2.0 },
        { "IQ2_XS", 2.25 },
        { "IQ2_S", 2.5 },
        { "IQ2_M", 2.75 },
        { "IQ3_XXS", 3.0 },
        { "IQ3_XS", 3.25 },
        { "IQ3_S", 3.5 },
        { "IQ3_M", 3.75 },
        { "IQ4_XS", 4.0 },
        { "IQ4_NL", 4.5 },

        // TQ (ternary quantization)
        { "TQ1_0", 1.69 },
        { "TQ2_0", 2.06 },

        // Full precision
        { "F16", 16.0 },
        { "F32", 32.0 },
        { "BF16", 16.0 },

        // Special cases (usually small tensors)
        { "I8", 8.0 },
        { "I16", 16.0 },
        { "I32", 32.0 },
    };

    public TensorAnalyzer(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "osync");
        }
    }

    /// <summary>
    /// Gets the dominant quantization type from model tensors via Ollama API.
    /// Returns format like "66% Q4_K" or null if unable to determine.
    /// </summary>
    public async Task<DominantQuantResult?> GetDominantQuantizationAsync(
        string modelName,
        string baseUrl = "http://localhost:11434",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tensors = await GetTensorsFromApiAsync(modelName, baseUrl, cancellationToken);

            if (tensors == null || tensors.Count == 0)
                return null;

            return CalculateDominantQuantization(tensors);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets tensor information from Ollama API with verbose=true.
    /// </summary>
    public async Task<List<TensorInfo>?> GetTensorsFromApiAsync(
        string modelName,
        string baseUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new { name = modelName, verbose = true };
            var response = await _httpClient.PostAsJsonAsync($"{baseUrl}/api/show", request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tensors", out var tensorsElement))
                return null;

            var tensors = new List<TensorInfo>();
            foreach (var tensor in tensorsElement.EnumerateArray())
            {
                var name = tensor.GetProperty("name").GetString() ?? "";
                var type = tensor.GetProperty("type").GetString() ?? "";
                var shape = new List<long>();

                if (tensor.TryGetProperty("shape", out var shapeElement))
                {
                    foreach (var dim in shapeElement.EnumerateArray())
                    {
                        shape.Add(dim.GetInt64());
                    }
                }

                tensors.Add(new TensorInfo(name, type, shape));
            }

            return tensors;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Calculates the dominant quantization type from a list of tensors.
    /// Only considers transformer block weight tensors (excludes embeddings, output, norms).
    /// Weights by tensor size (elements × bits per weight).
    /// </summary>
    /// <param name="tensors">List of tensor information</param>
    /// <param name="expectedQuantType">Expected quantization type from model name (maps "unknown" tensors to this)</param>
    public DominantQuantResult? CalculateDominantQuantization(List<TensorInfo> tensors, string? expectedQuantType = null)
    {
        if (tensors == null || tensors.Count == 0)
            return null;

        // Filter to only transformer block weight tensors
        // These are the tensors that represent the model's actual quantization
        var weightTensors = tensors.Where(IsTransformerWeight).ToList();

        if (weightTensors.Count == 0)
        {
            // Fallback to all tensors if no transformer weights found
            weightTensors = tensors;
        }

        // For mapping "unknown" tensors, use the normalized expected type with "?" suffix
        // to indicate uncertainty (e.g., "Q3_K?", "IQ2_S?", "TQ1_0?")
        var unknownMappedType = !string.IsNullOrEmpty(expectedQuantType)
            ? NormalizeQuantType(expectedQuantType) + "?"
            : null;

        // Calculate size in bytes for each quantization type
        var quantSizes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        double totalSize = 0;

        foreach (var tensor in weightTensors)
        {
            // Calculate number of elements
            long elements = 1;
            foreach (var dim in tensor.Shape)
            {
                elements *= dim;
            }

            // Get tensor type, mapping "unknown" or unrecognized types to expected type with "?" if provided
            var tensorType = tensor.Type?.Trim() ?? "";
            if (unknownMappedType != null &&
                (string.IsNullOrEmpty(tensorType) ||
                 tensorType.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
                 !QuantizationBitsPerWeight.ContainsKey(tensorType)))
            {
                // Map unknown/unrecognized types to expected quant with "?" suffix
                tensorType = unknownMappedType;
            }

            // Get bits per weight for this quantization type (strip ? for lookup)
            var typeForBits = tensorType.TrimEnd('?');
            var bitsPerWeight = GetBitsPerWeight(typeForBits);
            var sizeBytes = elements * bitsPerWeight / 8.0;

            // Keep the type as-is for display (including ? suffix for unknown tensors)
            var displayType = tensorType.ToUpperInvariant();

            if (!quantSizes.ContainsKey(displayType))
                quantSizes[displayType] = 0;

            quantSizes[displayType] += sizeBytes;
            totalSize += sizeBytes;
        }

        if (totalSize == 0)
            return null;

        // Find dominant quantization (excluding F32/F16 which are usually small tensors like norms)
        // Strip "?" suffix when checking for full precision
        var dominantQuant = quantSizes
            .Where(kvp => !IsFullPrecision(kvp.Key.TrimEnd('?')))
            .OrderByDescending(kvp => kvp.Value)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(dominantQuant.Key))
        {
            // All tensors are full precision, use the largest
            dominantQuant = quantSizes.OrderByDescending(kvp => kvp.Value).First();
        }

        var percentage = (dominantQuant.Value / totalSize) * 100;

        return new DominantQuantResult(
            dominantQuant.Key,
            (int)Math.Round(percentage),
            quantSizes.ToDictionary(kvp => kvp.Key, kvp => (int)Math.Round((kvp.Value / totalSize) * 100))
        );
    }

    /// <summary>
    /// Determines if a tensor is a transformer block weight tensor.
    /// Includes attention and FFN weights, excludes embeddings, output, norms, and biases.
    /// </summary>
    private static bool IsTransformerWeight(TensorInfo tensor)
    {
        var name = tensor.Name.ToLowerInvariant();

        // Exclude embeddings and output projection (often Q8_0 and very large)
        if (name.Contains("token_embd") || name.Contains("embed") ||
            name == "output.weight" || name.StartsWith("output."))
            return false;

        // Exclude norm layers (usually F32, small)
        if (name.Contains("norm"))
            return false;

        // Exclude bias tensors
        if (name.EndsWith(".bias") || name.Contains(".bias."))
            return false;

        // Include transformer block weights (attention and FFN)
        // Typical patterns: blk.*.attn_q.weight, blk.*.ffn_up.weight
        if (name.StartsWith("blk.") && name.EndsWith(".weight"))
            return true;

        // Also include encoder/decoder patterns for other architectures
        if ((name.StartsWith("encoder.") || name.StartsWith("decoder.")) && name.EndsWith(".weight"))
            return true;

        // Include layers.*.* patterns (some models use this)
        if (name.StartsWith("layers.") && name.EndsWith(".weight"))
            return true;

        return false;
    }

    /// <summary>
    /// Normalizes quantization type by removing size suffix (S/M/L/XL) for grouping.
    /// </summary>
    private static string NormalizeQuantType(string quantType)
    {
        // Keep IQ types as-is since they have different meanings (IQ1_S, IQ2_XXS, etc.)
        if (quantType.StartsWith("IQ", StringComparison.OrdinalIgnoreCase))
            return quantType.ToUpperInvariant();

        // Keep TQ (ternary quantization) types as-is (TQ1_0, TQ2_0)
        if (quantType.StartsWith("TQ", StringComparison.OrdinalIgnoreCase))
            return quantType.ToUpperInvariant();

        // For Q*_K_* types, normalize to Q*_K (e.g., Q3_K_M -> Q3_K)
        var match = QuantNormalizePattern().Match(quantType);
        if (match.Success)
        {
            return match.Groups[1].Value.ToUpperInvariant();
        }

        return quantType.ToUpperInvariant();
    }

    /// <summary>
    /// Gets bits per weight for a quantization type.
    /// </summary>
    private static double GetBitsPerWeight(string quantType)
    {
        if (QuantizationBitsPerWeight.TryGetValue(quantType, out var bits))
            return bits;

        // Try to match without suffix
        var normalized = NormalizeQuantType(quantType);
        if (QuantizationBitsPerWeight.TryGetValue(normalized, out bits))
            return bits;

        // Default to 4.5 bits (common Q4 quantization)
        return 4.5;
    }

    /// <summary>
    /// Checks if a quantization type is full precision (F32/F16/BF16).
    /// </summary>
    private static bool IsFullPrecision(string quantType)
    {
        return quantType.Equals("F32", StringComparison.OrdinalIgnoreCase) ||
               quantType.Equals("F16", StringComparison.OrdinalIgnoreCase) ||
               quantType.Equals("BF16", StringComparison.OrdinalIgnoreCase);
    }

    // Regex to normalize Q*_K_* to Q*_K
    [GeneratedRegex(@"^(Q\d+_K)(?:_[SMLX]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex QuantNormalizePattern();
}

/// <summary>
/// Represents a single tensor with its quantization info.
/// </summary>
public record TensorInfo(string Name, string Type, List<long> Shape);

/// <summary>
/// Result of dominant quantization analysis.
/// </summary>
public record DominantQuantResult(
    string DominantType,
    int Percentage,
    Dictionary<string, int> AllQuantPercentages)
{
    /// <summary>
    /// Gets a formatted string like "66% Q4_K".
    /// </summary>
    public string GetFormattedString()
    {
        return $"{Percentage}% {DominantType}";
    }

    /// <summary>
    /// Gets a formatted string with API quant type if different.
    /// Format: "Q6_K_XL (76% Q6_K)" or just "66% Q4_K" if API returned unknown.
    /// </summary>
    public string GetFormattedString(string? apiQuantType)
    {
        if (string.IsNullOrEmpty(apiQuantType) ||
            apiQuantType.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return GetFormattedString();
        }

        // If API quant type matches dominant type, just show percentage
        var normalizedApi = apiQuantType.ToUpperInvariant();
        var normalizedDominant = DominantType.ToUpperInvariant();

        if (normalizedApi == normalizedDominant || normalizedApi.StartsWith(normalizedDominant))
        {
            return $"{apiQuantType} ({Percentage}%)";
        }

        // Different types - show both
        return $"{apiQuantType} ({Percentage}% {DominantType})";
    }
}
