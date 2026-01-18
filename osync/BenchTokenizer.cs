using System.Text;
using System.Text.RegularExpressions;

namespace osync
{
    /// <summary>
    /// Tokenizer helper for context length estimation in benchmark tests.
    /// Provides approximate token counts using llama3.2-style tokenization heuristics.
    /// </summary>
    public static class BenchTokenizer
    {
        /// <summary>
        /// Default characters per token ratio for llama3/qwen models.
        /// Based on empirical testing with qwen2:7b: ~4.5 chars/token for English text.
        /// Using 4.2 as a slightly conservative estimate.
        /// </summary>
        public const double DefaultCharsPerToken = 4.2;

        /// <summary>
        /// Characters per token ratio for code content (typically more tokens due to symbols).
        /// </summary>
        public const double CodeCharsPerToken = 2.8;

        /// <summary>
        /// Characters per token ratio for structured/formatted text (JSON, markdown, etc.).
        /// </summary>
        public const double StructuredCharsPerToken = 3.0;

        /// <summary>
        /// Context-dependent chars/token ratios based on calibration with deepseek-r1:7b.
        /// Tokenizers become more efficient (higher chars/token) at larger context sizes.
        /// Format: (maxContextLength, charsPerToken) - uses ratio for contexts up to maxContextLength
        /// </summary>
        private static readonly (int maxContext, double ratio)[] ContextDependentRatios = new[]
        {
            (4096, 4.0),      // 2k-4k: ~4.0 chars/token
            (16384, 4.35),    // 8k-16k: ~4.3-4.4 chars/token
            (32768, 4.5),     // 32k: ~4.5 chars/token
            (65536, 4.6),     // 64k: ~4.6 chars/token
            (131072, 4.7),    // 128k: ~4.63 chars/token (calibrated)
            (262144, 4.7),    // 256k: ~4.7 chars/token (same as 128k)
            (int.MaxValue, 4.7)  // 512k+: ~4.7 chars/token (estimated)
        };

        /// <summary>
        /// Get the appropriate chars/token ratio for a given context size.
        /// Based on calibration data showing tokenizers are more efficient at larger contexts.
        /// </summary>
        /// <param name="contextLength">Target context length in tokens</param>
        /// <returns>Chars per token ratio optimized for that context size</returns>
        public static double GetCharsPerTokenForContext(int contextLength)
        {
            foreach (var (maxContext, ratio) in ContextDependentRatios)
            {
                if (contextLength <= maxContext)
                    return ratio;
            }
            return DefaultCharsPerToken;
        }

        /// <summary>
        /// Estimate token count for a given text using character-based heuristics.
        /// This is a fast approximation suitable for context budget estimation.
        /// </summary>
        /// <param name="text">The text to estimate tokens for</param>
        /// <param name="charsPerToken">Characters per token ratio (default: 4.2)</param>
        /// <returns>Estimated token count</returns>
        public static int EstimateTokenCount(string text, double charsPerToken = DefaultCharsPerToken)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return (int)Math.Ceiling(text.Length / charsPerToken);
        }

        /// <summary>
        /// Estimate token count using context-dependent chars/token ratio.
        /// Uses calibrated ratios based on target context size for better accuracy at larger contexts.
        /// </summary>
        /// <param name="text">The text to estimate tokens for</param>
        /// <param name="contextLength">Target context length in tokens (used to select appropriate ratio)</param>
        /// <returns>Estimated token count</returns>
        public static int EstimateTokenCountForContext(string text, int contextLength)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var ratio = GetCharsPerTokenForContext(contextLength);
            return (int)Math.Ceiling(text.Length / ratio);
        }

        /// <summary>
        /// Estimate token count with automatic content type detection.
        /// Adjusts chars/token ratio based on content characteristics.
        /// </summary>
        /// <param name="text">The text to estimate tokens for</param>
        /// <returns>Estimated token count</returns>
        public static int EstimateTokenCountSmart(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var ratio = DetectContentTypeRatio(text);
            return (int)Math.Ceiling(text.Length / ratio);
        }

        /// <summary>
        /// Detect content type and return appropriate chars/token ratio.
        /// </summary>
        private static double DetectContentTypeRatio(string text)
        {
            // Count special characters that indicate code or structured content
            var symbolCount = 0;
            var alphaCount = 0;
            var digitCount = 0;
            var spaceCount = 0;

            foreach (var c in text)
            {
                if (char.IsLetter(c)) alphaCount++;
                else if (char.IsDigit(c)) digitCount++;
                else if (char.IsWhiteSpace(c)) spaceCount++;
                else symbolCount++;
            }

            var total = text.Length;
            if (total == 0) return DefaultCharsPerToken;

            var symbolRatio = (double)symbolCount / total;
            var alphaRatio = (double)alphaCount / total;

            // High symbol ratio indicates code
            if (symbolRatio > 0.15)
                return CodeCharsPerToken;

            // JSON/structured detection (curly braces, brackets, colons)
            if (text.Contains('{') && text.Contains('}') && text.Contains(':'))
                return StructuredCharsPerToken;

            // Default for natural language
            return DefaultCharsPerToken;
        }

        /// <summary>
        /// Estimate tokens for a chat message (includes role overhead).
        /// Llama3 chat format adds ~4 tokens per message for role markers.
        /// </summary>
        /// <param name="role">Message role (user, assistant, system)</param>
        /// <param name="content">Message content</param>
        /// <returns>Estimated token count including overhead</returns>
        public static int EstimateChatMessageTokens(string role, string content)
        {
            // Role tokens overhead: <|start_header_id|>role<|end_header_id|>\n\n ... <|eot_id|>
            const int roleOverhead = 4;
            return EstimateTokenCount(content) + roleOverhead;
        }

        /// <summary>
        /// Estimate total tokens for a conversation.
        /// </summary>
        /// <param name="messages">List of (role, content) tuples</param>
        /// <returns>Estimated total token count</returns>
        public static int EstimateConversationTokens(IEnumerable<(string role, string content)> messages)
        {
            // BOS token at start
            var total = 1;

            foreach (var (role, content) in messages)
            {
                total += EstimateChatMessageTokens(role, content);
            }

            return total;
        }

        /// <summary>
        /// Calculate context budget for benchmark testing.
        /// Returns how many tokens are available for content given the target context length.
        /// </summary>
        /// <param name="targetContextLength">Target context length in tokens</param>
        /// <param name="questionCount">Number of questions to ask</param>
        /// <param name="estimatedResponseTokens">Estimated tokens per model response</param>
        /// <returns>Available tokens for story content</returns>
        public static int CalculateContentBudget(
            int targetContextLength,
            int questionCount,
            int estimatedResponseTokens = 150)
        {
            // Per-question overhead: question text (~20 tokens) + response (~150 tokens) + framing (~10 tokens)
            const int questionOverhead = 20;
            const int framingOverhead = 10;

            var qaOverhead = questionCount * (questionOverhead + estimatedResponseTokens + framingOverhead);

            // Reserve 5% safety margin
            var safetyMargin = (int)(targetContextLength * 0.05);

            return targetContextLength - qaOverhead - safetyMargin;
        }

        /// <summary>
        /// Generate text to fill a specific token budget.
        /// Useful for generating padding content in tests.
        /// </summary>
        /// <param name="targetTokens">Target number of tokens</param>
        /// <param name="charsPerToken">Characters per token ratio</param>
        /// <returns>Approximate character count needed</returns>
        public static int TokensToChars(int targetTokens, double charsPerToken = DefaultCharsPerToken)
        {
            return (int)(targetTokens * charsPerToken);
        }

        /// <summary>
        /// Validate that content fits within context budget.
        /// </summary>
        /// <param name="content">Content to validate</param>
        /// <param name="maxTokens">Maximum allowed tokens</param>
        /// <returns>(isValid, estimatedTokens, percentUsed)</returns>
        public static (bool isValid, int estimatedTokens, double percentUsed) ValidateContextFit(
            string content,
            int maxTokens)
        {
            var estimated = EstimateTokenCountSmart(content);
            var percent = maxTokens > 0 ? (estimated * 100.0 / maxTokens) : 0;
            return (estimated <= maxTokens, estimated, percent);
        }

        /// <summary>
        /// Get a summary of token estimation for verbose output.
        /// </summary>
        public static string GetTokenSummary(string content, int maxTokens)
        {
            var (isValid, estimated, percent) = ValidateContextFit(content, maxTokens);
            var status = isValid ? "OK" : "OVERFLOW";
            return $"Tokens: ~{estimated:N0}/{maxTokens:N0} ({percent:F1}%) [{status}]";
        }
    }
}
