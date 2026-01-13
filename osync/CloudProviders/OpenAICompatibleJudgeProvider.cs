// OpenAI-Compatible Judge Provider - For providers with OpenAI-compatible APIs
// Supports: Google Gemini, Mistral AI, Together AI, HuggingFace

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// Judge provider for OpenAI-compatible APIs (Gemini, Mistral, Together, HuggingFace)
    /// </summary>
    public class OpenAICompatibleJudgeProvider : CloudJudgeProviderBase
    {
        private readonly string _providerName;
        private readonly string _baseUrl;

        public override string ProviderName => _providerName;

        public override string[] EnvironmentVariables => _providerName.ToLowerInvariant() switch
        {
            "gemini" => new[] { "GEMINI_API_KEY", "GOOGLE_API_KEY" },
            "huggingface" => new[] { "HF_TOKEN", "HUGGINGFACE_TOKEN" },
            "mistral" => new[] { "MISTRAL_API_KEY" },
            "together" => new[] { "TOGETHER_API_KEY" },
            _ => Array.Empty<string>()
        };

        public OpenAICompatibleJudgeProvider(string providerName, string apiKey, string modelName, string baseUrl, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, modelName, apiKeyFromEnv, timeoutSeconds)
        {
            _providerName = providerName;
            _baseUrl = baseUrl.TrimEnd('/');

            // Set authorization header
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public override string? GetApiVersion() => "v1"; // OpenAI-compatible is always v1

        public override async Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync()
        {
            try
            {
                // Send a minimal request to validate credentials
                var request = new OpenAICompatibleRequest
                {
                    Model = _modelName,
                    Messages = new List<OpenAICompatibleMessage>
                    {
                        new() { Role = "user", Content = "Hi" }
                    },
                    MaxTokens = 10
                };

                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/chat/completions", request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (false, $"Authentication failed. Please check your API key from {GetApiKeySourceDescription()}.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // Check if it's a model error
                    if (errorContent.Contains("model", StringComparison.OrdinalIgnoreCase))
                    {
                        var models = await ListModelsAsync();
                        var modelList = models != null && models.Count > 0
                            ? $" Available models: {string.Join(", ", models.Take(10))}"
                            : "";
                        return (false, $"Model '{_modelName}' not found.{modelList}");
                    }

                    return (false, $"API error ({response.StatusCode}): {errorContent}");
                }

                return (true, null);
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Connection error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Error: {ex.Message}");
            }
        }

        public override async Task<List<string>?> ListModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/models");
                if (!response.IsSuccessStatusCode)
                    return GetKnownModels();

                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);

                var models = new List<string>();
                if (doc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    foreach (var model in dataElement.EnumerateArray())
                    {
                        if (model.TryGetProperty("id", out var idProp))
                        {
                            models.Add(idProp.GetString() ?? "");
                        }
                    }
                }

                return models.Count > 0 ? models : GetKnownModels();
            }
            catch
            {
                return GetKnownModels();
            }
        }

        private List<string>? GetKnownModels()
        {
            return _providerName.ToLowerInvariant() switch
            {
                "gemini" => new List<string>
                {
                    "gemini-2.0-flash",
                    "gemini-2.0-flash-exp",
                    "gemini-1.5-pro",
                    "gemini-1.5-flash",
                    "gemini-1.5-flash-8b"
                },
                "mistral" => new List<string>
                {
                    "mistral-large-latest",
                    "mistral-medium-latest",
                    "mistral-small-latest",
                    "codestral-latest",
                    "ministral-8b-latest",
                    "ministral-3b-latest"
                },
                "together" => new List<string>
                {
                    "meta-llama/Llama-3.3-70B-Instruct-Turbo",
                    "meta-llama/Meta-Llama-3.1-405B-Instruct-Turbo",
                    "meta-llama/Meta-Llama-3.1-70B-Instruct-Turbo",
                    "mistralai/Mixtral-8x22B-Instruct-v0.1",
                    "Qwen/Qwen2.5-72B-Instruct-Turbo"
                },
                "huggingface" => new List<string>
                {
                    "meta-llama/Llama-3.3-70B-Instruct",
                    "meta-llama/Meta-Llama-3.1-70B-Instruct",
                    "mistralai/Mixtral-8x7B-Instruct-v0.1",
                    "Qwen/Qwen2.5-72B-Instruct"
                },
                _ => null
            };
        }

        public override async Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            var request = new OpenAICompatibleRequest
            {
                Model = _modelName,
                Messages = new List<OpenAICompatibleMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                MaxTokens = maxTokens,
                Temperature = 0.0
            };

            var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/chat/completions", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            var content = "";
            if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                choices.GetArrayLength() > 0)
            {
                var firstChoice = choices[0];
                if (firstChoice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var contentProp))
                {
                    content = contentProp.GetString() ?? "";
                }
            }

            var rawResponse = content;

            // Try to find JSON in the response
            var jsonStart = content.IndexOf('{');
            var jsonEnd = content.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                content = content.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            return ParseJudgeResponse(content, rawResponse);
        }
    }

    // Request/Response models for OpenAI-compatible APIs
    internal class OpenAICompatibleRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public List<OpenAICompatibleMessage> Messages { get; set; } = new();

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    internal class OpenAICompatibleMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
