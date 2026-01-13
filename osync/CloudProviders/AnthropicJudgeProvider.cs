// Anthropic Claude Judge Provider - Uses Anthropic Messages API

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// Judge provider for Anthropic Claude using the Messages API
    /// </summary>
    public class AnthropicJudgeProvider : CloudJudgeProviderBase
    {
        private const string BASE_URL = "https://api.anthropic.com/v1";
        private const string API_VERSION = "2023-06-01";

        public override string ProviderName => "anthropic";
        public override string[] EnvironmentVariables => new[] { "ANTHROPIC_API_KEY" };

        public AnthropicJudgeProvider(string apiKey, string modelName, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, modelName, apiKeyFromEnv, timeoutSeconds)
        {
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", API_VERSION);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public override string? GetApiVersion() => API_VERSION;

        public override async Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync()
        {
            try
            {
                var request = new AnthropicRequest
                {
                    Model = _modelName,
                    MaxTokens = 10,
                    Messages = new List<AnthropicMessage>
                    {
                        new() { Role = "user", Content = "Hi" }
                    }
                };

                var response = await _httpClient.PostAsJsonAsync($"{BASE_URL}/messages", request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (false, $"Authentication failed. Please check your API key from {GetApiKeySourceDescription()}.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

                    // Check for model not found error
                    if (errorContent.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                        (errorContent.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                         errorContent.Contains("invalid", StringComparison.OrdinalIgnoreCase)))
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

        public override Task<List<string>?> ListModelsAsync()
        {
            // Return known Claude models (API doesn't have a public list endpoint)
            var models = new List<string>
            {
                "claude-sonnet-4-20250514",
                "claude-opus-4-20250514",
                "claude-3-7-sonnet-latest",
                "claude-3-5-sonnet-latest",
                "claude-3-5-haiku-latest",
                "claude-3-opus-latest",
                "claude-3-haiku-20240307"
            };
            return Task.FromResult<List<string>?>(models);
        }

        public override async Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            var request = new AnthropicRequest
            {
                Model = _modelName,
                MaxTokens = maxTokens,
                System = systemPrompt,
                Temperature = 0.0,
                Messages = new List<AnthropicMessage>
                {
                    new() { Role = "user", Content = userPrompt }
                }
            };

            var response = await _httpClient.PostAsJsonAsync($"{BASE_URL}/messages", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            // Extract text content from response
            var content = "";
            if (doc.RootElement.TryGetProperty("content", out var contentArray))
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == "text" &&
                        block.TryGetProperty("text", out var textProp))
                    {
                        content += textProp.GetString() ?? "";
                    }
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

    internal class AnthropicRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; }

        [JsonPropertyName("system")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? System { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("messages")]
        public List<AnthropicMessage> Messages { get; set; } = new();
    }

    internal class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }
}
