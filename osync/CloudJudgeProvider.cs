// Cloud Judge Provider - Interface and factory for cloud AI providers
// Supports: Anthropic, OpenAI, Azure OpenAI, Google Gemini, HuggingFace, Cohere, Mistral, Together AI, Replicate

using System.Text.Json;
using System.Text.RegularExpressions;

namespace osync
{
    /// <summary>
    /// Response from a cloud judge provider
    /// </summary>
    public record CloudJudgeResponse(
        int Score,
        string BestAnswer,
        string Reason,
        string? RawResponse = null
    );

    /// <summary>
    /// Parsed cloud provider argument
    /// </summary>
    public record CloudProviderConfig(
        string ProviderName,
        string ModelName,
        string? ApiKey,
        string? Endpoint,  // For Azure
        bool ApiKeyFromEnv
    );

    /// <summary>
    /// Interface for cloud AI judge providers
    /// </summary>
    public interface ICloudJudgeProvider
    {
        /// <summary>
        /// Provider name (e.g., "anthropic", "openai", "gemini")
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Environment variables used for API key (for error messages)
        /// </summary>
        string[] EnvironmentVariables { get; }

        /// <summary>
        /// Whether the API key was provided from environment variable
        /// </summary>
        bool ApiKeyFromEnv { get; }

        /// <summary>
        /// Validate connection and credentials
        /// </summary>
        Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync();

        /// <summary>
        /// List available models (if supported by the API)
        /// </summary>
        Task<List<string>?> ListModelsAsync();

        /// <summary>
        /// Execute judge prompt and get structured response
        /// </summary>
        Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get API version if available
        /// </summary>
        string? GetApiVersion();

        /// <summary>
        /// Get the model name being used
        /// </summary>
        string ModelName { get; }
    }

    /// <summary>
    /// Factory for creating cloud judge providers from command-line arguments
    /// </summary>
    public static class CloudJudgeProviderFactory
    {
        // Provider name aliases
        private static readonly Dictionary<string, string> ProviderAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            { "claude", "anthropic" },
            { "anthropic", "anthropic" },
            { "openai", "openai" },
            { "gpt", "openai" },
            { "gemini", "gemini" },
            { "google", "gemini" },
            { "huggingface", "huggingface" },
            { "hf", "huggingface" },
            { "azure", "azure" },
            { "azureopenai", "azure" },
            { "cohere", "cohere" },
            { "mistral", "mistral" },
            { "together", "together" },
            { "togetherai", "together" },
            { "replicate", "replicate" }
        };

        // Environment variables for each provider
        private static readonly Dictionary<string, string[]> ProviderEnvVars = new(StringComparer.OrdinalIgnoreCase)
        {
            { "anthropic", new[] { "ANTHROPIC_API_KEY" } },
            { "openai", new[] { "OPENAI_API_KEY" } },
            { "gemini", new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" } },
            { "huggingface", new[] { "HF_TOKEN", "HUGGINGFACE_TOKEN" } },
            { "azure", new[] { "AZURE_OPENAI_API_KEY" } },
            { "cohere", new[] { "CO_API_KEY", "COHERE_API_KEY" } },
            { "mistral", new[] { "MISTRAL_API_KEY" } },
            { "together", new[] { "TOGETHER_API_KEY" } },
            { "replicate", new[] { "REPLICATE_API_TOKEN" } }
        };

        /// <summary>
        /// Check if the argument is a cloud provider (starts with @)
        /// </summary>
        public static bool IsCloudProvider(string argument)
        {
            return !string.IsNullOrEmpty(argument) && argument.StartsWith("@");
        }

        /// <summary>
        /// Parse cloud provider argument: @provider[:token]/model or @provider[:key@endpoint]/model (for Azure)
        /// </summary>
        public static CloudProviderConfig? ParseArgument(string argument)
        {
            if (!IsCloudProvider(argument))
                return null;

            // Remove @ prefix
            var value = argument.Substring(1);

            // Parse format: provider[:token]/model or provider[:key@endpoint]/model
            string providerPart;
            string? tokenPart = null;
            string modelPart;

            // Check for token (contains :)
            var colonIndex = value.IndexOf(':');
            var slashIndex = value.IndexOf('/');

            if (slashIndex == -1)
            {
                // No model specified
                return null;
            }

            if (colonIndex != -1 && colonIndex < slashIndex)
            {
                // Has token: provider:token/model
                providerPart = value.Substring(0, colonIndex);
                tokenPart = value.Substring(colonIndex + 1, slashIndex - colonIndex - 1);
                modelPart = value.Substring(slashIndex + 1);
            }
            else
            {
                // No token: provider/model
                providerPart = value.Substring(0, slashIndex);
                modelPart = value.Substring(slashIndex + 1);
            }

            // Normalize provider name
            if (!ProviderAliases.TryGetValue(providerPart, out var normalizedProvider))
            {
                return null; // Unknown provider
            }

            // Handle Azure special format: key@endpoint
            string? endpoint = null;
            string? apiKey = tokenPart;
            bool apiKeyFromEnv = false;

            if (normalizedProvider == "azure" && tokenPart != null && tokenPart.Contains("@"))
            {
                var atIndex = tokenPart.IndexOf('@');
                apiKey = tokenPart.Substring(0, atIndex);
                endpoint = tokenPart.Substring(atIndex + 1);
                // Add https:// if not present
                if (!endpoint.StartsWith("http://") && !endpoint.StartsWith("https://"))
                {
                    endpoint = "https://" + endpoint;
                }
            }

            // If no API key provided, try environment variables
            if (string.IsNullOrEmpty(apiKey) && ProviderEnvVars.TryGetValue(normalizedProvider, out var envVars))
            {
                foreach (var envVar in envVars)
                {
                    var envValue = Environment.GetEnvironmentVariable(envVar);
                    if (!string.IsNullOrEmpty(envValue))
                    {
                        apiKey = envValue;
                        apiKeyFromEnv = true;
                        break;
                    }
                }

                // For Azure, also check endpoint env var
                if (normalizedProvider == "azure" && string.IsNullOrEmpty(endpoint))
                {
                    endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");
                }
            }

            return new CloudProviderConfig(
                ProviderName: normalizedProvider,
                ModelName: modelPart,
                ApiKey: apiKey,
                Endpoint: endpoint,
                ApiKeyFromEnv: apiKeyFromEnv
            );
        }

        /// <summary>
        /// Create a cloud judge provider from configuration
        /// </summary>
        public static ICloudJudgeProvider? CreateProvider(CloudProviderConfig config, int timeoutSeconds)
        {
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                return null; // No API key available
            }

            return config.ProviderName.ToLowerInvariant() switch
            {
                "anthropic" => new AnthropicJudgeProvider(config.ApiKey, config.ModelName, config.ApiKeyFromEnv, timeoutSeconds),
                "openai" => new OpenAIJudgeProvider(config.ApiKey, config.ModelName, config.ApiKeyFromEnv, timeoutSeconds),
                "azure" => config.Endpoint != null
                    ? new AzureOpenAIJudgeProvider(config.ApiKey, config.Endpoint, config.ModelName, config.ApiKeyFromEnv, timeoutSeconds)
                    : null,
                "gemini" => new OpenAICompatibleJudgeProvider("gemini", config.ApiKey, config.ModelName,
                    "https://generativelanguage.googleapis.com/v1beta/openai", config.ApiKeyFromEnv, timeoutSeconds),
                "huggingface" => new OpenAICompatibleJudgeProvider("huggingface", config.ApiKey, config.ModelName,
                    "https://router.huggingface.co/v1", config.ApiKeyFromEnv, timeoutSeconds),
                "mistral" => new OpenAICompatibleJudgeProvider("mistral", config.ApiKey, config.ModelName,
                    "https://api.mistral.ai/v1", config.ApiKeyFromEnv, timeoutSeconds),
                "together" => new OpenAICompatibleJudgeProvider("together", config.ApiKey, config.ModelName,
                    "https://api.together.xyz/v1", config.ApiKeyFromEnv, timeoutSeconds),
                "cohere" => new CohereJudgeProvider(config.ApiKey, config.ModelName, config.ApiKeyFromEnv, timeoutSeconds),
                "replicate" => new ReplicateJudgeProvider(config.ApiKey, config.ModelName, config.ApiKeyFromEnv, timeoutSeconds),
                _ => null
            };
        }

        /// <summary>
        /// Get environment variable names for a provider
        /// </summary>
        public static string[] GetEnvVarsForProvider(string providerName)
        {
            if (ProviderEnvVars.TryGetValue(providerName, out var envVars))
                return envVars;
            return Array.Empty<string>();
        }

        /// <summary>
        /// Get all supported provider names
        /// </summary>
        public static IEnumerable<string> GetSupportedProviders()
        {
            return ProviderEnvVars.Keys;
        }
    }

    /// <summary>
    /// Base class for cloud judge providers with common functionality
    /// </summary>
    public abstract class CloudJudgeProviderBase : ICloudJudgeProvider
    {
        protected readonly string _apiKey;
        protected readonly string _modelName;
        protected readonly bool _apiKeyFromEnv;
        protected readonly int _timeoutSeconds;
        protected readonly HttpClient _httpClient;

        public abstract string ProviderName { get; }
        public abstract string[] EnvironmentVariables { get; }
        public bool ApiKeyFromEnv => _apiKeyFromEnv;
        public string ModelName => _modelName;

        protected CloudJudgeProviderBase(string apiKey, string modelName, bool apiKeyFromEnv, int timeoutSeconds)
        {
            _apiKey = apiKey;
            _modelName = modelName;
            _apiKeyFromEnv = apiKeyFromEnv;
            _timeoutSeconds = timeoutSeconds;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        public abstract Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync();
        public abstract Task<List<string>?> ListModelsAsync();
        public abstract Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default);
        public abstract string? GetApiVersion();

        /// <summary>
        /// Parse judge response JSON to extract score, bestAnswer, and reason
        /// </summary>
        protected CloudJudgeResponse ParseJudgeResponse(string content, string rawResponse)
        {
            try
            {
                // Try to parse as JSON
                using var doc = JsonDocument.Parse(content);
                var root = doc.RootElement;

                int score = 50;
                string bestAnswer = "AB";
                string reason = "";

                // Try to get score
                if (root.TryGetProperty("score", out var scoreProp))
                {
                    if (scoreProp.ValueKind == JsonValueKind.Number)
                    {
                        var scoreValue = scoreProp.GetDouble();
                        // Handle 0-1 range
                        score = scoreValue <= 1.0 ? (int)(scoreValue * 100) : (int)scoreValue;
                        score = Math.Clamp(score, 1, 100);
                    }
                    else if (scoreProp.ValueKind == JsonValueKind.String && int.TryParse(scoreProp.GetString(), out var parsedScore))
                    {
                        score = Math.Clamp(parsedScore, 1, 100);
                    }
                }

                // Try to get bestanswer (case-insensitive)
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.Name.Equals("bestanswer", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("best_answer", StringComparison.OrdinalIgnoreCase))
                    {
                        var ba = prop.Value.GetString()?.Trim().ToUpperInvariant() ?? "AB";
                        bestAnswer = NormalizeBestAnswer(ba);
                        break;
                    }
                }

                // Try to get reason
                if (root.TryGetProperty("reason", out var reasonProp))
                {
                    reason = reasonProp.GetString() ?? "";
                }

                return new CloudJudgeResponse(score, bestAnswer, reason, rawResponse);
            }
            catch
            {
                // If JSON parsing fails, try regex extraction
                return ExtractWithRegex(content, rawResponse);
            }
        }

        private CloudJudgeResponse ExtractWithRegex(string content, string rawResponse)
        {
            int score = 50;
            string bestAnswer = "AB";
            string reason = "";

            // Extract score
            var scoreMatch = Regex.Match(content, @"[""']?score[""']?\s*[:\s]\s*(\d+)", RegexOptions.IgnoreCase);
            if (scoreMatch.Success && int.TryParse(scoreMatch.Groups[1].Value, out var parsedScore))
            {
                score = Math.Clamp(parsedScore, 1, 100);
            }

            // Extract bestanswer
            var bestMatch = Regex.Match(content, @"[""']?best\s*_?answer[""']?\s*[:\s]\s*[""']?([ABab]+|tie|equal)[""']?", RegexOptions.IgnoreCase);
            if (bestMatch.Success)
            {
                bestAnswer = NormalizeBestAnswer(bestMatch.Groups[1].Value.ToUpperInvariant());
            }

            // Extract reason
            var reasonMatch = Regex.Match(content, @"[""']?reason[""']?\s*[:\s]\s*[""'](.+?)[""']", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (reasonMatch.Success)
            {
                reason = reasonMatch.Groups[1].Value;
            }

            return new CloudJudgeResponse(score, bestAnswer, reason, rawResponse);
        }

        private static string NormalizeBestAnswer(string value)
        {
            return value.ToUpperInvariant() switch
            {
                "A" => "A",
                "B" => "B",
                "AB" or "BA" or "TIE" or "EQUAL" or "IDENTICAL" or "BOTH" or "DRAW" => "AB",
                _ => "AB"
            };
        }

        /// <summary>
        /// Get API key source description for error messages (never reveals the actual key)
        /// </summary>
        protected string GetApiKeySourceDescription()
        {
            if (_apiKeyFromEnv)
            {
                return $"environment variable ({string.Join(" or ", EnvironmentVariables)})";
            }
            return "command line";
        }
    }
}
