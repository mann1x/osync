using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// Test category containing related questions
    /// </summary>
    public class TestCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<TestQuestion> Questions { get; set; } = new List<TestQuestion>();

        /// <summary>
        /// Optional context length override for all questions in this category
        /// If set, overrides the test suite's default context length
        /// </summary>
        public int? ContextLength { get; set; }
    }

    /// <summary>
    /// Individual test question
    /// </summary>
    public class TestQuestion
    {
        public int CategoryId { get; set; }
        public int QuestionId { get; set; }

        [JsonIgnore]
        public string Id => $"{CategoryId}-{QuestionId}";

        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Optional context length override for this specific question
        /// If set, overrides both suite and category context length settings
        /// </summary>
        public int? ContextLength { get; set; }
    }

    /// <summary>
    /// External test suite JSON file structure
    /// </summary>
    public class ExternalTestSuiteJson
    {
        public string Name { get; set; } = string.Empty;
        public int NumPredict { get; set; } = 4096;

        /// <summary>
        /// Context length (num_ctx) for testing. Default is 4096.
        /// Can be overridden at category or question level.
        /// </summary>
        public int ContextLength { get; set; } = 4096;

        public List<TestCategory> Categories { get; set; } = new List<TestCategory>();
    }

    /// <summary>
    /// Complete results file structure
    /// </summary>
    public class QcResultsFile
    {
        public string TestSuiteName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        /// <summary>
        /// URL to the source repository for the model (e.g., HuggingFace page)
        /// </summary>
        public string? RepositoryUrl { get; set; }
        /// <summary>
        /// osync version used to run the tests
        /// </summary>
        public string? OsyncVersion { get; set; }
        /// <summary>
        /// Ollama server version used for testing base and quantizations
        /// </summary>
        public string? OllamaVersion { get; set; }
        /// <summary>
        /// Ollama server version used for judge model (similarity scoring)
        /// </summary>
        public string? OllamaJudgeVersion { get; set; }
        /// <summary>
        /// Ollama server version used for judge best answer model
        /// </summary>
        public string? OllamaJudgeBestAnswerVersion { get; set; }
        /// <summary>
        /// Maximum tokens to generate per answer (test suite setting)
        /// </summary>
        public int? NumPredict { get; set; }
        /// <summary>
        /// Default context length for the test suite
        /// </summary>
        public int? ContextLength { get; set; }
        public QcTestOptions Options { get; set; } = new QcTestOptions();
        public List<QuantResult> Results { get; set; } = new List<QuantResult>();
    }

    /// <summary>
    /// Test options used for generate API
    /// </summary>
    public class QcTestOptions
    {
        public double Temperature { get; set; }
        public int Seed { get; set; }
        public double TopP { get; set; }
        public int TopK { get; set; }
        public double? RepeatPenalty { get; set; }
        public double? FrequencyPenalty { get; set; }
    }

    /// <summary>
    /// Results for a single quantization
    /// </summary>
    public class QuantResult
    {
        public string Tag { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public long DiskSizeBytes { get; set; }
        /// <summary>
        /// Full SHA256 digest of the model manifest
        /// </summary>
        public string? Digest { get; set; }
        /// <summary>
        /// Short digest (first 12 characters of the SHA256)
        /// </summary>
        public string? ShortDigest { get; set; }
        public string Family { get; set; } = string.Empty;
        public string ParameterSize { get; set; } = string.Empty;
        public string QuantizationType { get; set; } = string.Empty;
        /// <summary>
        /// Enhanced quantization string from tensor analysis.
        /// Format: "66% Q4_K" or "Q6_K_XL (76% Q6_K)"
        /// </summary>
        public string? EnhancedQuantization { get; set; }
        public bool IsBase { get; set; }
        /// <summary>
        /// Indicates this model was pulled on-demand and should be removed after testing completes.
        /// Used for resume scenarios to track which models need cleanup.
        /// </summary>
        public bool PulledOnDemand { get; set; }
        public List<QuestionResult> QuestionResults { get; set; } = new List<QuestionResult>();
    }

    /// <summary>
    /// Result for a single question
    /// </summary>
    public class QuestionResult
    {
        public string QuestionId { get; set; } = string.Empty;  // Format: "1-1", "2-5"
        public string Category { get; set; } = string.Empty;
        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;
        public List<TokenLogprob> Tokens { get; set; } = new List<TokenLogprob>();
        public double EvalTokensPerSecond { get; set; }
        public double PromptTokensPerSecond { get; set; }
        public int TotalTokens { get; set; }
        /// <summary>
        /// Actual context length used for this question (resolved from suite/category/question overrides)
        /// </summary>
        public int? ContextLength { get; set; }
        public JudgmentResult? Judgment { get; set; }
    }

    /// <summary>
    /// Result from judge model evaluation
    /// </summary>
    public class JudgmentResult
    {
        public string JudgeModel { get; set; } = string.Empty;
        public int Score { get; set; }  // 1-100
        public string Reason { get; set; } = string.Empty;  // Judge's reasoning for the similarity score
        /// <summary>
        /// Which answer is qualitatively better: "A" (base), "B" (quant), or "AB" (tie)
        /// </summary>
        public string? BestAnswer { get; set; }
        /// <summary>
        /// Model used to determine the best answer (when --judgebest is used)
        /// </summary>
        public string? JudgeModelBestAnswer { get; set; }
        /// <summary>
        /// Reasoning from the best answer judge model
        /// </summary>
        public string? ReasonBestAnswer { get; set; }
        public DateTime JudgedAt { get; set; }
        /// <summary>
        /// Timestamp when best answer judgment was performed (when --judgebest is used)
        /// </summary>
        public DateTime? JudgedBestAnswerAt { get; set; }
        /// <summary>
        /// Raw JSON response from judge, only populated when reason parsing failed (for debugging)
        /// </summary>
        [JsonIgnore]
        public string? RawResponse { get; set; }

        /// <summary>
        /// Judge provider type: "ollama" or cloud provider name (e.g., "anthropic", "openai", "gemini")
        /// Nullable for backward compatibility - null/missing assumes "ollama"
        /// </summary>
        public string? JudgeProvider { get; set; }

        /// <summary>
        /// Cloud API version if available (e.g., "2023-06-01" for Anthropic)
        /// </summary>
        public string? JudgeApiVersion { get; set; }

        /// <summary>
        /// Provider for best answer judge (when --judgebest is used)
        /// Nullable for backward compatibility - null/missing assumes "ollama"
        /// </summary>
        public string? JudgeBestProvider { get; set; }

        /// <summary>
        /// API version for best answer judge
        /// </summary>
        public string? JudgeBestApiVersion { get; set; }
    }

    /// <summary>
    /// Token with its logprob value
    /// </summary>
    public class TokenLogprob
    {
        public string Token { get; set; } = string.Empty;
        public double Logprob { get; set; }
        public List<int>? Bytes { get; set; }
    }

    /// <summary>
    /// Ollama generate API request for logprobs
    /// </summary>
    public class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;

        [JsonPropertyName("logprobs")]
        public bool Logprobs { get; set; } = false;

        [JsonPropertyName("options")]
        public OllamaGenerateOptions? Options { get; set; }
    }

    /// <summary>
    /// Generate API options
    /// </summary>
    public class OllamaGenerateOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }

        [JsonPropertyName("seed")]
        public int Seed { get; set; }

        [JsonPropertyName("top_p")]
        public double TopP { get; set; }

        [JsonPropertyName("top_k")]
        public int TopK { get; set; }

        [JsonPropertyName("repeat_penalty")]
        public double? RepeatPenalty { get; set; }

        [JsonPropertyName("frequency_penalty")]
        public double? FrequencyPenalty { get; set; }

        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; set; }

        [JsonPropertyName("num_ctx")]
        public int? NumCtx { get; set; }
    }

    /// <summary>
    /// Ollama generate API streaming response
    /// </summary>
    public class OllamaGenerateResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("response")]
        public string? Response { get; set; }

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("logprobs")]
        public List<ResponseLogprob>? Logprobs { get; set; }

        // Final response fields (when done=true)
        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; set; }

        [JsonPropertyName("load_duration")]
        public long? LoadDuration { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("prompt_eval_duration")]
        public long? PromptEvalDuration { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long? EvalDuration { get; set; }
    }

    /// <summary>
    /// Logprob information in generate response
    /// </summary>
    public class ResponseLogprob
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("logprob")]
        public double Logprob { get; set; }

        [JsonPropertyName("bytes")]
        public List<int>? Bytes { get; set; }

        [JsonPropertyName("top_logprobs")]
        public List<TopLogprob>? TopLogprobs { get; set; }
    }

    /// <summary>
    /// Top alternative tokens with logprobs
    /// </summary>
    public class TopLogprob
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = string.Empty;

        [JsonPropertyName("logprob")]
        public double Logprob { get; set; }
    }

    /// <summary>
    /// Scoring results for qcview
    /// </summary>
    public class QcScoringResults
    {
        public string BaseModelName { get; set; } = string.Empty;
        public string BaseTag { get; set; } = string.Empty;
        public string BaseFamily { get; set; } = string.Empty;
        public string BaseParameterSize { get; set; } = string.Empty;
        public long BaseDiskSizeBytes { get; set; }
        public string BaseQuantizationType { get; set; } = string.Empty;
        public double BaseEvalTokensPerSecond { get; set; }
        public double BasePromptTokensPerSecond { get; set; }
        public QcTestOptions Options { get; set; } = new QcTestOptions();
        public string TestSuiteName { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public List<QuantScoreResult> QuantScores { get; set; } = new List<QuantScoreResult>();

        // Judgment scoring info
        public bool HasJudgmentScoring { get; set; }
        public string? JudgeModel { get; set; }
        public string? JudgeModelBestAnswer { get; set; }

        // Cloud provider info (only set when using cloud providers, null for ollama)
        public string? JudgeProvider { get; set; }
        public string? JudgeApiVersion { get; set; }
        public string? JudgeBestProvider { get; set; }
        public string? JudgeBestApiVersion { get; set; }

        /// <summary>
        /// URL to the source repository for the model
        /// </summary>
        public string? RepositoryUrl { get; set; }

        // Version information
        public string? OsyncVersion { get; set; }
        public string? OllamaVersion { get; set; }
        public string? OllamaJudgeVersion { get; set; }
        public string? OllamaJudgeBestAnswerVersion { get; set; }
    }

    /// <summary>
    /// Score results for a single quantization
    /// </summary>
    public class QuantScoreResult
    {
        public string Tag { get; set; } = string.Empty;
        public long DiskSizeBytes { get; set; }
        public string QuantizationType { get; set; } = string.Empty;
        /// <summary>
        /// Enhanced quantization string from tensor analysis.
        /// Format: "66% Q4_K" or "Q6_K_XL (76% Q6_K)"
        /// </summary>
        public string? EnhancedQuantization { get; set; }

        // Category confidence scores (%) - metrics only
        public Dictionary<string, double> CategoryScores { get; set; } = new Dictionary<string, double>();

        // Category judgment scores (%) - when judge model is used
        public Dictionary<string, double> CategoryJudgmentScores { get; set; } = new Dictionary<string, double>();

        // Overall confidence score (%)
        public double TotalConfidenceScore { get; set; }

        // Judgment scoring (when judge model is used)
        public double? AverageJudgmentScore { get; set; }
        public bool HasJudgmentScoring { get; set; }

        // Best answer statistics (when judge model is used)
        /// <summary>Count of questions where quant answer was better than base</summary>
        public int BestCount { get; set; }
        /// <summary>Count of questions where quant answer was worse than base</summary>
        public int WorstCount { get; set; }
        /// <summary>Count of questions where quant and base answers were tied</summary>
        public int TieCount { get; set; }
        /// <summary>Percentage of quant wins (excluding ties): BestCount / (BestCount + WorstCount) * 100</summary>
        public double? BestPercentage { get; set; }
        /// <summary>Percentage of quant losses (excluding ties): WorstCount / (BestCount + WorstCount) * 100</summary>
        public double? WorstPercentage { get; set; }
        /// <summary>Percentage of ties out of total judged questions</summary>
        public double? TiePercentage { get; set; }

        // Category-level best answer stats
        public Dictionary<string, CategoryBestStats> CategoryBestStats { get; set; } = new Dictionary<string, CategoryBestStats>();

        // Final combined score (50% metrics + 50% judgment when available)
        public double FinalScore { get; set; }

        // Performance metrics
        public double EvalTokensPerSecond { get; set; }
        public double PromptTokensPerSecond { get; set; }
        public double EvalPerformancePercent { get; set; }
        public double PromptPerformancePercent { get; set; }

        // Detailed per-question scores (for JSON output)
        public List<QuestionScore>? QuestionScores { get; set; }
    }

    /// <summary>
    /// Best answer statistics for a category
    /// </summary>
    public class CategoryBestStats
    {
        public int BestCount { get; set; }
        public int WorstCount { get; set; }
        public int TieCount { get; set; }
        public double? BestPercentage { get; set; }
    }

    /// <summary>
    /// Score for a single question comparison
    /// </summary>
    public class QuestionScore
    {
        public string QuestionId { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double TokenSimilarityScore { get; set; }
        public double LogprobsDivergenceScore { get; set; }
        public double LengthConsistencyScore { get; set; }
        public double PerplexityScore { get; set; }
        public double OverallConfidenceScore { get; set; }
        public double? JudgmentScore { get; set; }
        /// <summary>
        /// Which answer is qualitatively better: "A" (base), "B" (quant), or "AB" (tie)
        /// </summary>
        public string? BestAnswer { get; set; }
    }
}
