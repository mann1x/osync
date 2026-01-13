// Replicate Judge Provider - Uses Replicate async predictions API with polling

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// Judge provider for Replicate using their async predictions API
    /// </summary>
    public class ReplicateJudgeProvider : CloudJudgeProviderBase
    {
        private const string BASE_URL = "https://api.replicate.com/v1";
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxPollTime = TimeSpan.FromSeconds(300);

        public override string ProviderName => "replicate";
        public override string[] EnvironmentVariables => new[] { "REPLICATE_API_TOKEN" };

        public ReplicateJudgeProvider(string apiKey, string modelName, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, modelName, apiKeyFromEnv, timeoutSeconds)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Token", apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public override string? GetApiVersion() => "v1";

        public override async Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync()
        {
            try
            {
                // Check if the model exists by trying to get its info
                // Model format: owner/name or owner/name:version
                var modelPath = _modelName.Contains(":")
                    ? _modelName.Split(':')[0]
                    : _modelName;

                var response = await _httpClient.GetAsync($"{BASE_URL}/models/{modelPath}");

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    return (false, $"Authentication failed. Please check your API token from {GetApiKeySourceDescription()}.");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var models = await ListModelsAsync();
                    var modelList = models != null && models.Count > 0
                        ? $" Example models: {string.Join(", ", models.Take(5))}"
                        : "";
                    return (false, $"Model '{_modelName}' not found.{modelList}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
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
            // Return known popular Replicate models for text generation
            var models = new List<string>
            {
                "meta/llama-2-70b-chat",
                "meta/llama-2-13b-chat",
                "meta/meta-llama-3-70b-instruct",
                "meta/meta-llama-3-8b-instruct",
                "mistralai/mistral-7b-instruct-v0.2",
                "mistralai/mixtral-8x7b-instruct-v0.1"
            };
            return Task.FromResult<List<string>?>(models);
        }

        public override async Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            // Build the prompt - Replicate models typically expect a single prompt
            var fullPrompt = $"{systemPrompt}\n\n{userPrompt}";

            // Create prediction request
            var request = new ReplicatePredictionRequest
            {
                Model = _modelName,
                Input = new ReplicateInput
                {
                    Prompt = fullPrompt,
                    MaxNewTokens = maxTokens,
                    Temperature = 0.01 // Replicate may not accept exactly 0
                }
            };

            // Submit prediction
            var createResponse = await _httpClient.PostAsJsonAsync($"{BASE_URL}/predictions", request, cancellationToken);
            createResponse.EnsureSuccessStatusCode();

            var predictionContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            using var predictionDoc = JsonDocument.Parse(predictionContent);

            var predictionId = predictionDoc.RootElement.GetProperty("id").GetString();
            var getUrl = predictionDoc.RootElement.GetProperty("urls").GetProperty("get").GetString();

            // Poll for completion
            var startTime = DateTime.UtcNow;
            string status = "starting";
            JsonDocument? resultDoc = null;

            while (status != "succeeded" && status != "failed" && status != "canceled")
            {
                if (DateTime.UtcNow - startTime > MaxPollTime)
                {
                    throw new TimeoutException($"Replicate prediction timed out after {MaxPollTime.TotalSeconds} seconds");
                }

                cancellationToken.ThrowIfCancellationRequested();
                await Task.Delay(PollInterval, cancellationToken);

                var statusResponse = await _httpClient.GetAsync(getUrl, cancellationToken);
                statusResponse.EnsureSuccessStatusCode();

                var statusContent = await statusResponse.Content.ReadAsStringAsync(cancellationToken);
                resultDoc?.Dispose();
                resultDoc = JsonDocument.Parse(statusContent);
                status = resultDoc.RootElement.GetProperty("status").GetString() ?? "failed";
            }

            if (resultDoc == null)
            {
                throw new Exception("Failed to get prediction result");
            }

            using (resultDoc)
            {
                if (status == "failed")
                {
                    var error = resultDoc.RootElement.TryGetProperty("error", out var errorProp)
                        ? errorProp.GetString()
                        : "Unknown error";
                    throw new Exception($"Replicate prediction failed: {error}");
                }

                if (status == "canceled")
                {
                    throw new OperationCanceledException("Replicate prediction was canceled");
                }

                // Extract output - can be string or array of strings
                var content = "";
                if (resultDoc.RootElement.TryGetProperty("output", out var outputProp))
                {
                    if (outputProp.ValueKind == JsonValueKind.String)
                    {
                        content = outputProp.GetString() ?? "";
                    }
                    else if (outputProp.ValueKind == JsonValueKind.Array)
                    {
                        // Join array elements (streaming output)
                        var parts = new List<string>();
                        foreach (var part in outputProp.EnumerateArray())
                        {
                            if (part.ValueKind == JsonValueKind.String)
                            {
                                parts.Add(part.GetString() ?? "");
                            }
                        }
                        content = string.Join("", parts);
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
    }

    internal class ReplicatePredictionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("input")]
        public ReplicateInput Input { get; set; } = new();
    }

    internal class ReplicateInput
    {
        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("max_new_tokens")]
        public int MaxNewTokens { get; set; }

        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }
}
