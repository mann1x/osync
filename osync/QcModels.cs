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
    }

    /// <summary>
    /// External test suite JSON file structure
    /// </summary>
    public class ExternalTestSuiteJson
    {
        public string Name { get; set; } = string.Empty;
        public List<TestCategory> Categories { get; set; } = new List<TestCategory>();
    }

    /// <summary>
    /// Complete results file structure
    /// </summary>
    public class QcResultsFile
    {
        public string TestSuiteName { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
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
        public long DiskSizeBytes { get; set; }
        public string Family { get; set; } = string.Empty;
        public string ParameterSize { get; set; } = string.Empty;
        public string QuantizationType { get; set; } = string.Empty;
        public bool IsBase { get; set; }
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
    }

    /// <summary>
    /// Score results for a single quantization
    /// </summary>
    public class QuantScoreResult
    {
        public string Tag { get; set; } = string.Empty;
        public long DiskSizeBytes { get; set; }
        public string QuantizationType { get; set; } = string.Empty;

        // Category confidence scores (%)
        public Dictionary<string, double> CategoryScores { get; set; } = new Dictionary<string, double>();

        // Overall confidence score (%)
        public double TotalConfidenceScore { get; set; }

        // Performance metrics
        public double EvalTokensPerSecond { get; set; }
        public double PromptTokensPerSecond { get; set; }
        public double EvalPerformancePercent { get; set; }
        public double PromptPerformancePercent { get; set; }

        // Detailed per-question scores (for JSON output)
        public List<QuestionScore>? QuestionScores { get; set; }
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
    }
}
