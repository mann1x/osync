using System.Text.Json.Serialization;

namespace osync
{
    #region Test Suite Models

    /// <summary>
    /// Root structure for benchmark test suite JSON files
    /// </summary>
    public class BenchTestSuite
    {
        /// <summary>
        /// Type of benchmark test (e.g., "ctxbench", "ctxtoolsbench")
        /// </summary>
        public string TestType { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable description of the test
        /// </summary>
        public string TestDescription { get; set; } = string.Empty;

        /// <summary>
        /// Maximum context length supported by this test suite
        /// </summary>
        public int MaxContextLength { get; set; }

        /// <summary>
        /// Whether a judge model is required for this test type
        /// </summary>
        public bool JudgeRequired { get; set; }

        /// <summary>
        /// Default judge prompt template with substitution patterns:
        /// %%QUESTION%%, %%REF_ANSWER%%, %%MODEL_ANSWER%%
        /// </summary>
        public string? JudgePrompt { get; set; }

        /// <summary>
        /// Default judge system prompt
        /// </summary>
        public string? JudgeSystemPrompt { get; set; }

        /// <summary>
        /// Whether tools are enabled for this test type
        /// </summary>
        public bool ToolsEnabled { get; set; }

        /// <summary>
        /// List of tool names to enable (null = all available tools)
        /// </summary>
        public List<string>? EnabledTools { get; set; }

        /// <summary>
        /// System prompt to use for the test model (optional)
        /// </summary>
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Instructions to send at the start of the conversation (as first user message)
        /// </summary>
        public string? Instructions { get; set; }

        /// <summary>
        /// Maximum tokens to generate per answer. Optional - if not set, Ollama uses its default.
        /// </summary>
        public int? NumPredict { get; set; }

        /// <summary>
        /// Context length overhead to add when thinking mode is NOT detected.
        /// This accounts for Q&amp;A overhead, system prompts, etc.
        /// Default is 2048 tokens (2K).
        /// </summary>
        public int ContextLengthOverhead { get; set; } = 2048;

        /// <summary>
        /// Context length overhead to add when thinking mode IS detected.
        /// Thinking models need more context for internal reasoning.
        /// Default is 4096 tokens (4K).
        /// </summary>
        public int ContextLengthOverheadThinking { get; set; } = 4096;

        /// <summary>
        /// Categories in sequential order (2k, 4k, 8k, etc.)
        /// </summary>
        public List<BenchCategory> Categories { get; set; } = new();
    }

    /// <summary>
    /// A category in the benchmark (maps to context size)
    /// </summary>
    public class BenchCategory
    {
        /// <summary>
        /// Category name (e.g., "2k", "4k", "8k")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Target context length for this category
        /// </summary>
        public int ContextLength { get; set; }

        /// <summary>
        /// Context text to inject (story content)
        /// </summary>
        public string Context { get; set; } = string.Empty;

        /// <summary>
        /// Optional override for judge prompt at category level
        /// </summary>
        public string? JudgePrompt { get; set; }

        /// <summary>
        /// Optional override for judge system prompt at category level
        /// </summary>
        public string? JudgeSystemPrompt { get; set; }

        /// <summary>
        /// Questions for categories without subcategories (e.g., 2k)
        /// </summary>
        public List<BenchQuestion>? Questions { get; set; }

        /// <summary>
        /// Subcategories (Old, New) for categories with previous context
        /// </summary>
        public List<BenchSubCategory>? SubCategories { get; set; }
    }

    /// <summary>
    /// A subcategory within a category (Old or New)
    /// </summary>
    public class BenchSubCategory
    {
        /// <summary>
        /// Subcategory name ("Old" or "New")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// For "Old" subcategory: which previous categories these questions reference
        /// </summary>
        public List<string>? AboutCategories { get; set; }

        /// <summary>
        /// Optional override for judge prompt at subcategory level
        /// </summary>
        public string? JudgePrompt { get; set; }

        /// <summary>
        /// Optional override for judge system prompt at subcategory level
        /// </summary>
        public string? JudgeSystemPrompt { get; set; }

        /// <summary>
        /// Questions in this subcategory
        /// </summary>
        public List<BenchQuestion> Questions { get; set; } = new();
    }

    /// <summary>
    /// A single question in the benchmark
    /// </summary>
    public class BenchQuestion
    {
        /// <summary>
        /// Question ID within the category/subcategory
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// The question text to ask the model
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The correct/reference answer for judgment
        /// </summary>
        public string ReferenceAnswer { get; set; } = string.Empty;

        /// <summary>
        /// For "Old" questions: which category this question is about
        /// </summary>
        public string? AboutCategory { get; set; }

        /// <summary>
        /// Optional override for judge prompt at question level
        /// </summary>
        public string? JudgePrompt { get; set; }

        /// <summary>
        /// Optional override for judge system prompt at question level
        /// </summary>
        public string? JudgeSystemPrompt { get; set; }

        /// <summary>
        /// For ctxtoolsbench: which tools should be used (for validation/hints)
        /// </summary>
        public List<string>? ExpectedTools { get; set; }
    }

    #endregion

    #region Results Models

    /// <summary>
    /// Complete benchmark results file structure
    /// </summary>
    public class BenchResultsFile
    {
        /// <summary>
        /// Name of the test suite used
        /// </summary>
        public string TestSuiteName { get; set; } = string.Empty;

        /// <summary>
        /// SHA256 digest of the test suite file content (for validation)
        /// </summary>
        public string? TestSuiteDigest { get; set; }

        /// <summary>
        /// Test type (e.g., "ctxbench", "ctxtoolsbench")
        /// </summary>
        public string TestType { get; set; } = string.Empty;

        /// <summary>
        /// Human-readable test description
        /// </summary>
        public string TestDescription { get; set; } = string.Empty;

        /// <summary>
        /// Base model name (without tag)
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// Judge model used for scoring
        /// </summary>
        public string? JudgeModel { get; set; }

        /// <summary>
        /// Judge provider (ollama, anthropic, openai, etc.)
        /// </summary>
        public string? JudgeProvider { get; set; }

        /// <summary>
        /// Judge API version (for cloud providers)
        /// </summary>
        public string? JudgeApiVersion { get; set; }

        /// <summary>
        /// Test options used
        /// </summary>
        public BenchTestOptions Options { get; set; } = new();

        /// <summary>
        /// When the test was started
        /// </summary>
        public DateTime TestedAt { get; set; }

        /// <summary>
        /// osync version used for testing
        /// </summary>
        public string? OsyncVersion { get; set; }

        /// <summary>
        /// Ollama version used for testing
        /// </summary>
        public string? OllamaVersion { get; set; }

        /// <summary>
        /// Ollama version used for judge (if different)
        /// </summary>
        public string? OllamaJudgeVersion { get; set; }

        /// <summary>
        /// Ollama server URL used for testing
        /// </summary>
        public string? TestServerUrl { get; set; }

        /// <summary>
        /// Ollama server URL used for judge (if different from test server)
        /// </summary>
        public string? JudgeServerUrl { get; set; }

        /// <summary>
        /// Maximum context length from test suite
        /// </summary>
        public int MaxContextLength { get; set; }

        /// <summary>
        /// Category limit if -L was specified
        /// </summary>
        public string? CategoryLimit { get; set; }

        /// <summary>
        /// Results per quantization/model
        /// </summary>
        public List<BenchQuantResult> Results { get; set; } = new();
    }

    /// <summary>
    /// Test options used during benchmark
    /// </summary>
    public class BenchTestOptions
    {
        public double? Temperature { get; set; }
        public int? Seed { get; set; }
        public double? TopP { get; set; }
        public int? TopK { get; set; }
        public double? RepeatPenalty { get; set; }
        public double? FrequencyPenalty { get; set; }
        public int Timeout { get; set; } = 1800;

        /// <summary>
        /// Whether thinking mode is enabled (for thinking models like qwen3, deepseek-r1). Default is false.
        /// </summary>
        public bool EnableThinking { get; set; }

        /// <summary>
        /// Thinking level for models that support it (e.g., low, medium, high).
        /// When set, overrides EnableThinking for models like GPT-OSS that require level instead of bool.
        /// </summary>
        public string? ThinkLevel { get; set; }
    }

    /// <summary>
    /// Cached results from pre-flight checks (thinking detection, context resolution, tools validation)
    /// </summary>
    public class PreflightCheckResult
    {
        /// <summary>
        /// Model digest (SHA256) used to verify it's the same model
        /// </summary>
        public string? ModelDigest { get; set; }

        /// <summary>
        /// Whether thinking mode was detected for this model
        /// </summary>
        public bool ThinkingEnabled { get; set; }

        /// <summary>
        /// Effective context overhead (standard or thinking)
        /// </summary>
        public int EffectiveOverhead { get; set; }

        /// <summary>
        /// Model's reported maximum context length
        /// </summary>
        public int ModelMaxContextLength { get; set; }

        /// <summary>
        /// Effective max context length after applying -L limit
        /// </summary>
        public int EffectiveMaxContext { get; set; }

        /// <summary>
        /// Whether tools pre-flight test passed (for ctxtoolsbench)
        /// </summary>
        public bool? ToolsPreflightPassed { get; set; }

        /// <summary>
        /// When the pre-flight check was performed
        /// </summary>
        public DateTime? CheckedAt { get; set; }
    }

    /// <summary>
    /// Results for a single quantization/model
    /// </summary>
    public class BenchQuantResult
    {
        /// <summary>
        /// Full model tag (e.g., "qwen3:4b-q4_k_m")
        /// </summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>
        /// Model name without tag
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// Repository URL for this specific model/quant
        /// </summary>
        public string? RepositoryUrl { get; set; }

        /// <summary>
        /// Model size on disk in bytes
        /// </summary>
        public long DiskSizeBytes { get; set; }

        /// <summary>
        /// Model digest
        /// </summary>
        public string? Digest { get; set; }

        /// <summary>
        /// Short digest (first 12 chars)
        /// </summary>
        public string? ShortDigest { get; set; }

        /// <summary>
        /// Model family
        /// </summary>
        public string Family { get; set; } = string.Empty;

        /// <summary>
        /// Parameter size
        /// </summary>
        public string ParameterSize { get; set; } = string.Empty;

        /// <summary>
        /// Quantization type
        /// </summary>
        public string QuantizationType { get; set; } = string.Empty;

        /// <summary>
        /// Enhanced quantization string from tensor analysis
        /// </summary>
        public string? EnhancedQuantization { get; set; }

        /// <summary>
        /// Whether this model was pulled on-demand
        /// </summary>
        public bool PulledOnDemand { get; set; }

        /// <summary>
        /// Pre-flight check results (cached to avoid re-running)
        /// </summary>
        public PreflightCheckResult? PreflightResult { get; set; }

        /// <summary>
        /// Overall score across all categories (0-100)
        /// </summary>
        public double OverallScore { get; set; }

        /// <summary>
        /// Total questions answered
        /// </summary>
        public int TotalQuestions { get; set; }

        /// <summary>
        /// Total correct answers
        /// </summary>
        public int CorrectAnswers { get; set; }

        /// <summary>
        /// Results per category
        /// </summary>
        public List<BenchCategoryResult> CategoryResults { get; set; } = new();

        /// <summary>
        /// When testing started for this quant
        /// </summary>
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// When testing completed for this quant
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Whether testing is complete
        /// </summary>
        public bool IsComplete { get; set; }

        #region Per-Tag Metadata (v1.2.9+)

        /// <summary>
        /// Judge model used for this tag
        /// </summary>
        public string? JudgeModel { get; set; }

        /// <summary>
        /// Judge provider (ollama, anthropic, openai, etc.)
        /// </summary>
        public string? JudgeProvider { get; set; }

        /// <summary>
        /// Test server URL used for this tag
        /// </summary>
        public string? TestServerUrl { get; set; }

        /// <summary>
        /// osync version used for testing this tag
        /// </summary>
        public string? OsyncVersion { get; set; }

        /// <summary>
        /// Ollama version on test server
        /// </summary>
        public string? OllamaVersion { get; set; }

        /// <summary>
        /// Ollama version on judge server
        /// </summary>
        public string? OllamaJudgeVersion { get; set; }

        /// <summary>
        /// Test options used for this tag
        /// </summary>
        public BenchTagOptions? TestOptions { get; set; }

        #endregion
    }

    /// <summary>
    /// Test options captured per-tag
    /// </summary>
    public class BenchTagOptions
    {
        public int Seed { get; set; }
        public int Timeout { get; set; }
        public bool EnableThinking { get; set; }
        public string? ThinkLevel { get; set; }
        public double? Temperature { get; set; }
    }

    /// <summary>
    /// Results for a category
    /// </summary>
    public class BenchCategoryResult
    {
        /// <summary>
        /// Category name (e.g., "2k", "4k")
        /// </summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Target context length for this category
        /// </summary>
        public int TargetContextLength { get; set; }

        /// <summary>
        /// Actual context tokens used (from Ollama API)
        /// </summary>
        public int? ContextTokensUsed { get; set; }

        /// <summary>
        /// Category score (0-100)
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Total questions in this category
        /// </summary>
        public int TotalQuestions { get; set; }

        /// <summary>
        /// Correct answers in this category
        /// </summary>
        public int CorrectAnswers { get; set; }

        /// <summary>
        /// Question results (for categories without subcategories)
        /// </summary>
        public List<BenchQuestionResult>? QuestionResults { get; set; }

        /// <summary>
        /// Subcategory results (for categories with Old/New)
        /// </summary>
        public List<BenchSubCategoryResult>? SubCategoryResults { get; set; }

        /// <summary>
        /// Whether this category is complete
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Average response time across all questions (milliseconds)
        /// </summary>
        public double? AvgResponseTimeMs { get; set; }

        /// <summary>
        /// Average prompt processing speed (tokens/second)
        /// </summary>
        public double? AvgPromptToksPerSec { get; set; }

        /// <summary>
        /// Average response generation speed (tokens/second)
        /// </summary>
        public double? AvgEvalToksPerSec { get; set; }

        /// <summary>
        /// Total prompt tokens processed in this category
        /// </summary>
        public int? TotalPromptTokens { get; set; }

        /// <summary>
        /// Total completion tokens generated in this category
        /// </summary>
        public int? TotalCompletionTokens { get; set; }

        /// <summary>
        /// Total thinking tokens generated in this category (cumulative)
        /// </summary>
        public int? TotalThinkingTokens { get; set; }

        /// <summary>
        /// Peak thinking tokens in any single response in this category
        /// </summary>
        public int? PeakThinkingTokens { get; set; }

        /// <summary>
        /// Peak context tokens used in any single request in this category
        /// </summary>
        public int? PeakContextTokens { get; set; }

        /// <summary>
        /// Context usage as percentage of target for this category
        /// </summary>
        public double? ContextUsagePercent { get; set; }

        /// <summary>
        /// Whether context overflowed the target length at any point
        /// </summary>
        public bool? ContextOverflowed { get; set; }
    }

    /// <summary>
    /// Results for a subcategory
    /// </summary>
    public class BenchSubCategoryResult
    {
        /// <summary>
        /// Subcategory name ("Old" or "New")
        /// </summary>
        public string SubCategory { get; set; } = string.Empty;

        /// <summary>
        /// Subcategory score (0-100)
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Total questions in this subcategory
        /// </summary>
        public int TotalQuestions { get; set; }

        /// <summary>
        /// Correct answers in this subcategory
        /// </summary>
        public int CorrectAnswers { get; set; }

        /// <summary>
        /// Question results
        /// </summary>
        public List<BenchQuestionResult> QuestionResults { get; set; } = new();
    }

    /// <summary>
    /// Result for a single question
    /// </summary>
    public class BenchQuestionResult
    {
        /// <summary>
        /// Question ID
        /// </summary>
        public int QuestionId { get; set; }

        /// <summary>
        /// The question text
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Reference/correct answer
        /// </summary>
        public string ReferenceAnswer { get; set; } = string.Empty;

        /// <summary>
        /// Model's answer
        /// </summary>
        public string ModelAnswer { get; set; } = string.Empty;

        /// <summary>
        /// Model's thinking/reasoning trace (when thinking mode is enabled)
        /// </summary>
        public string? ModelThinking { get; set; }

        /// <summary>
        /// Score for this question (0 or 100)
        /// </summary>
        public double Score { get; set; }

        /// <summary>
        /// Judge evaluation result
        /// </summary>
        public BenchJudgment? Judgment { get; set; }

        /// <summary>
        /// Tools used by the model for this question
        /// </summary>
        public List<BenchToolUsage> ToolsUsed { get; set; } = new();

        /// <summary>
        /// For "Old" questions: which category this was about
        /// </summary>
        public string? AboutCategory { get; set; }

        /// <summary>
        /// Response time in milliseconds
        /// </summary>
        public long ResponseTimeMs { get; set; }

        /// <summary>
        /// Tokens used for prompt
        /// </summary>
        public int? PromptTokens { get; set; }

        /// <summary>
        /// Tokens generated in response
        /// </summary>
        public int? CompletionTokens { get; set; }

        /// <summary>
        /// Tokens used for thinking/reasoning (when thinking mode is enabled)
        /// </summary>
        public int? ThinkingTokens { get; set; }

        /// <summary>
        /// Prompt processing speed (tokens/second)
        /// </summary>
        public double? PromptToksPerSec { get; set; }

        /// <summary>
        /// Response generation speed (tokens/second)
        /// </summary>
        public double? EvalToksPerSec { get; set; }

        /// <summary>
        /// Total duration from Ollama API (milliseconds)
        /// </summary>
        public long? TotalDurationMs { get; set; }

        /// <summary>
        /// Load duration from Ollama API (milliseconds) - model loading time
        /// </summary>
        public long? LoadDurationMs { get; set; }

        /// <summary>
        /// Context tokens used for this question (prompt + output at response time)
        /// </summary>
        public int? ContextTokensUsed { get; set; }

        /// <summary>
        /// Indicates if this question resulted in an error (e.g., API error, model failure)
        /// </summary>
        public bool IsError { get; set; }
    }

    /// <summary>
    /// Judge evaluation result
    /// </summary>
    public class BenchJudgment
    {
        /// <summary>
        /// Judge's answer: "YES" (correct) or "NO" (incorrect)
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// Judge's reasoning
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// When judgment was made
        /// </summary>
        public DateTime JudgedAt { get; set; }

        /// <summary>
        /// Raw JSON response from judge (for debugging)
        /// </summary>
        [JsonIgnore]
        public string? RawResponse { get; set; }
    }

    /// <summary>
    /// Tool usage tracking
    /// </summary>
    public class BenchToolUsage
    {
        /// <summary>
        /// Tool name that was called
        /// </summary>
        public string ToolName { get; set; } = string.Empty;

        /// <summary>
        /// Number of times this tool was called in this turn
        /// </summary>
        public int CallCount { get; set; }

        /// <summary>
        /// Parameters passed to the tool (last call if multiple)
        /// </summary>
        public Dictionary<string, object>? Parameters { get; set; }

        /// <summary>
        /// Arguments as JSON string for display
        /// </summary>
        public string? ArgumentsJson { get; set; }

        /// <summary>
        /// Result returned by the tool (last call if multiple)
        /// </summary>
        public string? Result { get; set; }
    }

    #endregion

    #region Scoring Models

    /// <summary>
    /// Calculated scoring results for benchview
    /// </summary>
    public class BenchScoringResults
    {
        public string TestSuiteName { get; set; } = string.Empty;
        public string TestType { get; set; } = string.Empty;
        public string TestDescription { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string? JudgeModel { get; set; }
        public string? JudgeProvider { get; set; }
        public string? JudgeApiVersion { get; set; }
        public BenchTestOptions Options { get; set; } = new();
        public DateTime TestedAt { get; set; }
        public string? OsyncVersion { get; set; }
        public string? OllamaVersion { get; set; }
        public int MaxContextLength { get; set; }
        public string? CategoryLimit { get; set; }
        public int TotalQuants { get; set; }
        public int TotalCategories { get; set; }

        /// <summary>
        /// Scored results per quantization
        /// </summary>
        public List<BenchQuantScoreResult> QuantScores { get; set; } = new();
    }

    /// <summary>
    /// Scored results for a quantization
    /// </summary>
    public class BenchQuantScoreResult
    {
        public string Tag { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string? RepositoryUrl { get; set; }
        public long DiskSizeBytes { get; set; }
        public string ParameterSize { get; set; } = string.Empty;
        public string QuantizationType { get; set; } = string.Empty;
        public string? EnhancedQuantization { get; set; }
        public double OverallScore { get; set; }
        public int TotalQuestions { get; set; }
        public int CorrectAnswers { get; set; }

        /// <summary>
        /// Scores per category
        /// </summary>
        public Dictionary<string, double> CategoryScores { get; set; } = new();

        /// <summary>
        /// Scores per subcategory (key: "category/subcategory")
        /// </summary>
        public Dictionary<string, double> SubCategoryScores { get; set; } = new();

        /// <summary>
        /// Context tokens used per category
        /// </summary>
        public Dictionary<string, int> ContextTokensUsed { get; set; } = new();

        /// <summary>
        /// Minimum prompt tokens/sec per category
        /// </summary>
        public Dictionary<string, double> MinPromptToksPerSec { get; set; } = new();

        /// <summary>
        /// Minimum eval tokens/sec per category
        /// </summary>
        public Dictionary<string, double> MinEvalToksPerSec { get; set; } = new();

        /// <summary>
        /// Average response time (ms) per category
        /// </summary>
        public Dictionary<string, double> AvgResponseTimeMs { get; set; } = new();
    }

    #endregion

    #region Context Tracking

    /// <summary>
    /// Tracks context usage across multi-turn conversations for benchmark testing.
    /// Helps verify that test suites don't overflow the intended context length.
    /// </summary>
    public class ContextTracker
    {
        /// <summary>
        /// Target context length for this category (used for percentage calculations)
        /// </summary>
        public int MaxContext { get; private set; }

        /// <summary>
        /// Actual configured context limit (used for overflow detection)
        /// </summary>
        public int ActualMaxContext { get; private set; }

        /// <summary>
        /// Tokens used for the prompt in the last response
        /// </summary>
        public int PromptTokens { get; private set; }

        /// <summary>
        /// Tokens generated in the last response
        /// </summary>
        public int OutputTokens { get; private set; }

        /// <summary>
        /// Thinking tokens in the last response (estimated from thinking content)
        /// </summary>
        public int ThinkingTokens { get; private set; }

        /// <summary>
        /// Cumulative thinking tokens across all responses in this category
        /// </summary>
        public int CumulativeThinkingTokens { get; private set; }

        /// <summary>
        /// Total tokens used (prompt + output, thinking is part of output)
        /// </summary>
        public int TotalUsed => PromptTokens + OutputTokens;

        /// <summary>
        /// Context usage as percentage of target context (MaxContext)
        /// </summary>
        public double UsagePercent => MaxContext > 0 ? (TotalUsed * 100.0 / MaxContext) : 0;

        /// <summary>
        /// Remaining context tokens available
        /// </summary>
        public int Remaining => MaxContext - TotalUsed;

        /// <summary>
        /// Peak prompt tokens observed in any single response
        /// </summary>
        public int PeakPromptTokens { get; private set; }

        /// <summary>
        /// Peak output tokens observed in any single response
        /// </summary>
        public int PeakOutputTokens { get; private set; }

        /// <summary>
        /// Peak total tokens observed in any single response
        /// </summary>
        public int PeakTotalUsed { get; private set; }

        /// <summary>
        /// Peak thinking tokens observed in any single response
        /// </summary>
        public int PeakThinkingTokens { get; private set; }

        /// <summary>
        /// Whether context has overflowed the actual configured limit at any point
        /// </summary>
        public bool HasOverflowed { get; private set; }

        /// <summary>
        /// Create a context tracker
        /// </summary>
        /// <param name="targetContext">Target context for this category (for percentage calculations)</param>
        /// <param name="actualMaxContext">Actual configured context limit (for overflow detection). If 0, uses targetContext.</param>
        public ContextTracker(int targetContext, int actualMaxContext = 0)
        {
            MaxContext = targetContext;
            ActualMaxContext = actualMaxContext > 0 ? actualMaxContext : targetContext;
        }

        /// <summary>
        /// Update tracker from Ollama API response metrics
        /// </summary>
        public void UpdateFromResponse(int? promptEvalCount, int? evalCount, int? thinkingTokens = null)
        {
            PromptTokens = promptEvalCount ?? 0;
            OutputTokens = evalCount ?? 0;
            ThinkingTokens = thinkingTokens ?? 0;

            // Accumulate thinking tokens
            CumulativeThinkingTokens += ThinkingTokens;

            // Track peak usage
            if (PromptTokens > PeakPromptTokens)
                PeakPromptTokens = PromptTokens;
            if (OutputTokens > PeakOutputTokens)
                PeakOutputTokens = OutputTokens;
            if (TotalUsed > PeakTotalUsed)
                PeakTotalUsed = TotalUsed;
            if (ThinkingTokens > PeakThinkingTokens)
                PeakThinkingTokens = ThinkingTokens;

            // Check for overflow against actual configured limit (not category target)
            if (TotalUsed > ActualMaxContext)
                HasOverflowed = true;
        }

        /// <summary>
        /// Reset tracker for a new conversation/category
        /// </summary>
        public void Reset()
        {
            PromptTokens = 0;
            OutputTokens = 0;
            ThinkingTokens = 0;
            CumulativeThinkingTokens = 0;
        }

        /// <summary>
        /// Reset all tracking including peaks (for new model)
        /// </summary>
        public void ResetAll()
        {
            Reset();
            PeakPromptTokens = 0;
            PeakOutputTokens = 0;
            PeakTotalUsed = 0;
            PeakThinkingTokens = 0;
            HasOverflowed = false;
        }

        /// <summary>
        /// Get a summary string for verbose output (uses invariant culture to avoid locale issues with Spectre.Console)
        /// </summary>
        public string GetSummary()
        {
            var overflowWarning = HasOverflowed ? " OVERFLOW!" : "";
            var thinkingInfo = ThinkingTokens > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " thinking={0} (cumul={1})", ThinkingTokens, CumulativeThinkingTokens)
                : (CumulativeThinkingTokens > 0 ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " thinking=0 (cumul={0})", CumulativeThinkingTokens) : "");
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Context: {0}/{1} ({2:F1}%) prompt={3} output={4}{5}{6}",
                TotalUsed, MaxContext, UsagePercent, PromptTokens, OutputTokens, thinkingInfo, overflowWarning);
        }

        /// <summary>
        /// Get peak usage summary string (uses invariant culture to avoid locale issues with Spectre.Console)
        /// </summary>
        public string GetPeakSummary()
        {
            var overflowWarning = HasOverflowed ? " OVERFLOW DETECTED!" : "";
            var peakPercent = MaxContext > 0 ? PeakTotalUsed * 100.0 / MaxContext : 0;
            var thinkingInfo = CumulativeThinkingTokens > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, " thinking: peak={0} total={1}", PeakThinkingTokens, CumulativeThinkingTokens)
                : "";
            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Peak: {0}/{1} ({2:F1}%){3}{4}",
                PeakTotalUsed, MaxContext, peakPercent, thinkingInfo, overflowWarning);
        }
    }

    #endregion

    #region Calibration Models

    /// <summary>
    /// Root structure for calibration data file
    /// </summary>
    public class CalibrationData
    {
        /// <summary>
        /// Full model name with tag (e.g., "deepseek-r1:7b")
        /// </summary>
        public string ModelName { get; set; } = string.Empty;

        /// <summary>
        /// When calibration was performed
        /// </summary>
        public DateTime CalibratedAt { get; set; }

        /// <summary>
        /// osync version used
        /// </summary>
        public string OsyncVersion { get; set; } = string.Empty;

        /// <summary>
        /// Chars per token ratio used for estimation
        /// </summary>
        public double CharsPerTokenRatio { get; set; }

        /// <summary>
        /// Calibration results per category
        /// </summary>
        public Dictionary<string, CalibrationCategory> Categories { get; set; } = new();
    }

    /// <summary>
    /// Calibration data for a single category
    /// </summary>
    public class CalibrationCategory
    {
        /// <summary>
        /// Category name (e.g., "2k")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Target context length for this category
        /// </summary>
        public int TargetContextLength { get; set; }

        /// <summary>
        /// Target fill percentage (0.90 or 0.95)
        /// </summary>
        public double TargetFillPercent { get; set; }

        /// <summary>
        /// Step-by-step token tracking
        /// </summary>
        public List<CalibrationStep> Steps { get; set; } = new();

        /// <summary>
        /// Summary statistics for this category
        /// </summary>
        public CalibrationSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// A single step in calibration tracking
    /// </summary>
    public class CalibrationStep
    {
        /// <summary>
        /// Step name (e.g., "Instructions", "Context", "Q1", "A1")
        /// </summary>
        public string StepName { get; set; } = string.Empty;

        /// <summary>
        /// Step type: "instructions", "context", "question", "answer"
        /// </summary>
        public string StepType { get; set; } = string.Empty;

        /// <summary>
        /// Character count of this component
        /// </summary>
        public int CharCount { get; set; }

        /// <summary>
        /// Estimated tokens for this component alone
        /// </summary>
        public int EstimatedTokens { get; set; }

        /// <summary>
        /// Cumulative estimated tokens up to this step
        /// </summary>
        public int CumulativeEstimate { get; set; }

        /// <summary>
        /// Actual prompt tokens from API (after this step)
        /// </summary>
        public int ActualPromptTokens { get; set; }

        /// <summary>
        /// Actual output tokens from API (after this step)
        /// </summary>
        public int ActualOutputTokens { get; set; }

        /// <summary>
        /// Delta between cumulative estimate and actual prompt tokens
        /// </summary>
        public int Delta { get; set; }

        /// <summary>
        /// Delta as percentage
        /// </summary>
        public double DeltaPercent { get; set; }

        /// <summary>
        /// Effective chars/token ratio calculated from this step
        /// </summary>
        public double EffectiveCharsPerToken { get; set; }

        /// <summary>
        /// Truncated text of the component (first 100 chars)
        /// </summary>
        public string TextPreview { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary statistics for calibration
    /// </summary>
    public class CalibrationSummary
    {
        /// <summary>
        /// Total characters in all components
        /// </summary>
        public int TotalChars { get; set; }

        /// <summary>
        /// Total estimated tokens
        /// </summary>
        public int TotalEstimatedTokens { get; set; }

        /// <summary>
        /// Final actual prompt tokens (from last API call)
        /// </summary>
        public int FinalActualPromptTokens { get; set; }

        /// <summary>
        /// Final actual output tokens (from last API call)
        /// </summary>
        public int FinalActualOutputTokens { get; set; }

        /// <summary>
        /// Overall delta (actual - estimated)
        /// </summary>
        public int OverallDelta { get; set; }

        /// <summary>
        /// Overall delta as percentage
        /// </summary>
        public double OverallDeltaPercent { get; set; }

        /// <summary>
        /// Average effective chars/token ratio
        /// </summary>
        public double AvgEffectiveCharsPerToken { get; set; }

        /// <summary>
        /// Recommended chars/token ratio based on calibration
        /// </summary>
        public double RecommendedCharsPerToken { get; set; }

        /// <summary>
        /// Context fill achieved as percentage of target
        /// </summary>
        public double ContextFillPercent { get; set; }

        /// <summary>
        /// Whether calibration is within tolerance (5%)
        /// </summary>
        public bool WithinTolerance { get; set; }
    }

    #endregion
}
