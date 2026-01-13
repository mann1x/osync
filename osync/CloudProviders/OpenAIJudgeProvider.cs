// OpenAI Judge Provider - Uses official OpenAI .NET SDK

using OpenAI;
using OpenAI.Chat;

namespace osync
{
    /// <summary>
    /// Judge provider for OpenAI using the official SDK
    /// </summary>
    public class OpenAIJudgeProvider : CloudJudgeProviderBase
    {
        private readonly ChatClient _chatClient;
        private readonly OpenAIClient _openAIClient;

        public override string ProviderName => "openai";
        public override string[] EnvironmentVariables => new[] { "OPENAI_API_KEY" };

        public OpenAIJudgeProvider(string apiKey, string modelName, bool apiKeyFromEnv, int timeoutSeconds)
            : base(apiKey, modelName, apiKeyFromEnv, timeoutSeconds)
        {
            _openAIClient = new OpenAIClient(apiKey);
            _chatClient = _openAIClient.GetChatClient(modelName);
        }

        public override string? GetApiVersion() => null; // OpenAI doesn't expose version

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
            catch (UnauthorizedAccessException)
            {
                return (false, $"Authentication failed. Please check your API key from {GetApiKeySourceDescription()}.");
            }
            catch (Exception ex) when (ex.Message.Contains("model") || ex.Message.Contains("Model"))
            {
                var models = await ListModelsAsync();
                var modelList = models != null && models.Count > 0
                    ? $" Available models: {string.Join(", ", models.Take(10))}"
                    : "";
                return (false, $"Model '{_modelName}' not found or not accessible.{modelList}");
            }
            catch (Exception ex)
            {
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public override Task<List<string>?> ListModelsAsync()
        {
            // Return known models - OpenAI SDK doesn't have a simple model listing method
            return Task.FromResult<List<string>?>(new List<string>
            {
                "gpt-4o",
                "gpt-4o-mini",
                "gpt-4-turbo",
                "gpt-4",
                "gpt-3.5-turbo",
                "o1",
                "o1-mini",
                "o1-preview"
            });
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
