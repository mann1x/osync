// Cohere Judge Provider - Uses Cohere Chat API

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// Judge provider for Cohere using their Chat API
    /// </summary>
    public class CohereJudgeProvider : CloudJudgeProviderBase
    {
        private const string BASE_URL = "https://api.cohere.com/v1";

        public override string ProviderName => "cohere";
        public override string[] EnvironmentVariables => new[] { "CO_API_KEY", "COHERE_API_KEY" };

        public CohereJudgeProvider(string apiKey, string modelName, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, modelName, apiKeyFromEnv, timeoutSeconds)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public override string? GetApiVersion() => "v1";

        public override async Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync()
        {
            try
            {
                var request = new CohereChatRequest
                {
                    Model = _modelName,
                    Message = "Hi"
                };

                var response = await _httpClient.PostAsJsonAsync($"{BASE_URL}/chat", request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (false, $"Authentication failed. Please check your API key from {GetApiKeySourceDescription()}.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();

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

        public override Task<List<string>?> ListModelsAsync()
        {
            // Return known Cohere models
            var models = new List<string>
            {
                "command-a-03-2025",
                "command-r-plus-08-2024",
                "command-r-plus",
                "command-r-08-2024",
                "command-r",
                "command-light",
                "command"
            };
            return Task.FromResult<List<string>?>(models);
        }

        public override async Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            var request = new CohereChatRequest
            {
                Model = _modelName,
                Message = userPrompt,
                Preamble = systemPrompt,
                Temperature = 0.0
            };

            var response = await _httpClient.PostAsJsonAsync($"{BASE_URL}/chat", request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseContent);

            var content = "";
            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                content = textProp.GetString() ?? "";
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

    internal class CohereChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("preamble")]
        public string? Preamble { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }
}
