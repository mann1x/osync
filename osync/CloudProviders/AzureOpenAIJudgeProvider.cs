// Azure OpenAI Judge Provider - Uses official Azure.AI.OpenAI SDK

using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;

namespace osync
{
    /// <summary>
    /// Judge provider for Azure OpenAI using the official SDK
    /// </summary>
    public class AzureOpenAIJudgeProvider : CloudJudgeProviderBase
    {
        private readonly AzureOpenAIClient _client;
        private readonly ChatClient _chatClient;
        private readonly string _endpoint;

        public override string ProviderName => "azure";
        public override string[] EnvironmentVariables => new[] { "AZURE_OPENAI_API_KEY", "AZURE_OPENAI_ENDPOINT" };

        public AzureOpenAIJudgeProvider(string apiKey, string endpoint, string deploymentName, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, deploymentName, apiKeyFromEnv, timeoutSeconds)
        {
            _endpoint = endpoint;

            var credential = new AzureKeyCredential(apiKey);
            _client = new AzureOpenAIClient(new Uri(endpoint), credential);
            _chatClient = _client.GetChatClient(deploymentName);
        }

        public override string? GetApiVersion() => "2024-10-21"; // Azure OpenAI API version

        public override async Task<(bool Success, string? ErrorMessage)> ValidateConnectionAsync()
        {
            try
            {
                // Send a minimal request to validate credentials
                var messages = new List<OpenAI.Chat.ChatMessage>
                {
                    new UserChatMessage("Hi")
                };

                var options = new ChatCompletionOptions
                {
                    MaxOutputTokenCount = 10
                };

                var response = await _chatClient.CompleteChatAsync(messages, options);
                return (true, null);
            }
            catch (RequestFailedException ex) when (ex.Status == 401 || ex.Status == 403)
            {
                return (false, $"Authentication failed. Please check your API key from {GetApiKeySourceDescription()} and endpoint '{_endpoint}'.");
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                return (false, $"Deployment '{_modelName}' not found at endpoint '{_endpoint}'. Please verify the deployment name and endpoint.");
            }
            catch (RequestFailedException ex)
            {
                return (false, $"Azure API error ({ex.Status}): {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public override Task<List<string>?> ListModelsAsync()
        {
            // Azure deployments are user-defined, can't list them via API easily
            // Return null to indicate listing is not supported
            return Task.FromResult<List<string>?>(null);
        }

        public override async Task<CloudJudgeResponse> JudgeAsync(string systemPrompt, string userPrompt, int maxTokens, CancellationToken cancellationToken = default)
        {
            var messages = new List<OpenAI.Chat.ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt)
            };

            var options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = maxTokens,
                Temperature = 0.0f
            };

            var response = await _chatClient.CompleteChatAsync(messages, options, cancellationToken);

            var content = response.Value.Content[0].Text ?? "";
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
