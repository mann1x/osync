namespace osync.Tests.Support;

/// <summary>
/// Builder/factory methods for constructing test data structures
/// used by BenchScoring and QcScoring unit tests.
/// </summary>
public static class TestHelpers
{
    // ── Bench data builders ─────────────────────────────────────

    public static BenchQuestionResult CreateBenchQuestion(
        int id = 1,
        double score = 80,
        double? promptToksPerSec = 100,
        double? evalToksPerSec = 50,
        long responseTimeMs = 1000,
        int? contextTokensUsed = null)
    {
        return new BenchQuestionResult
        {
            QuestionId = id,
            Question = $"Question {id}",
            ReferenceAnswer = $"Reference answer {id}",
            ModelAnswer = $"Model answer {id}",
            Score = score,
            ResponseTimeMs = responseTimeMs,
            PromptToksPerSec = promptToksPerSec,
            EvalToksPerSec = evalToksPerSec,
            ContextTokensUsed = contextTokensUsed
        };
    }

    public static BenchCategoryResult CreateBenchCategory(
        string category = "General Knowledge",
        int targetContextLength = 4096,
        double score = 80,
        int totalQuestions = 3,
        int correctAnswers = 2,
        double? avgResponseTimeMs = 1500,
        int? contextTokensUsed = 2048,
        List<BenchQuestionResult>? questions = null,
        List<BenchSubCategoryResult>? subCategories = null)
    {
        questions ??= Enumerable.Range(1, totalQuestions)
            .Select(i => CreateBenchQuestion(id: i, score: score))
            .ToList();

        return new BenchCategoryResult
        {
            Category = category,
            TargetContextLength = targetContextLength,
            Score = score,
            TotalQuestions = totalQuestions,
            CorrectAnswers = correctAnswers,
            AvgResponseTimeMs = avgResponseTimeMs,
            ContextTokensUsed = contextTokensUsed,
            QuestionResults = subCategories == null ? questions : null,
            SubCategoryResults = subCategories
        };
    }

    public static BenchSubCategoryResult CreateBenchSubCategory(
        string name = "Old",
        double score = 75,
        int totalQuestions = 2,
        List<BenchQuestionResult>? questions = null)
    {
        questions ??= Enumerable.Range(1, totalQuestions)
            .Select(i => CreateBenchQuestion(id: i, score: score))
            .ToList();

        return new BenchSubCategoryResult
        {
            SubCategory = name,
            Score = score,
            TotalQuestions = totalQuestions,
            CorrectAnswers = (int)(totalQuestions * score / 100),
            QuestionResults = questions
        };
    }

    public static BenchQuantResult CreateBenchQuantResult(
        string tag = "Q4_K_M",
        double overallScore = 75,
        List<BenchCategoryResult>? categories = null)
    {
        categories ??= new List<BenchCategoryResult>
        {
            CreateBenchCategory(category: "General Knowledge", targetContextLength: 4096, score: 80),
            CreateBenchCategory(category: "Reasoning", targetContextLength: 8192, score: 70)
        };

        return new BenchQuantResult
        {
            Tag = tag,
            ModelName = $"test-model:{tag}",
            DiskSizeBytes = 4_000_000_000,
            Family = "llama",
            ParameterSize = "7B",
            QuantizationType = tag,
            OverallScore = overallScore,
            TotalQuestions = categories.Sum(c => c.TotalQuestions),
            CorrectAnswers = categories.Sum(c => c.CorrectAnswers),
            CategoryResults = categories
        };
    }

    public static BenchResultsFile CreateBenchResultsFile(
        List<BenchQuantResult>? results = null)
    {
        results ??= new List<BenchQuantResult>
        {
            CreateBenchQuantResult(tag: "Q4_K_M", overallScore: 75),
            CreateBenchQuantResult(tag: "Q5_K_M", overallScore: 85)
        };

        return new BenchResultsFile
        {
            TestSuiteName = "test-suite",
            TestType = "benchmark",
            TestDescription = "Test benchmark",
            ModelName = "test-model",
            Options = new BenchTestOptions { Temperature = 0.0, Seed = 42 },
            TestedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            MaxContextLength = 8192,
            Results = results
        };
    }

    // ── QC data builders ────────────────────────────────────────

    public static List<TokenLogprob> CreateTokenLogprobs(
        int count = 10, double avgLogprob = -1.0)
    {
        // Generate tokens with logprobs centered around avgLogprob
        return Enumerable.Range(0, count)
            .Select(i => new TokenLogprob
            {
                Token = $"token{i}",
                Logprob = avgLogprob + (i % 2 == 0 ? 0.1 : -0.1)
            })
            .ToList();
    }

    public static QuestionResult CreateQuestionResult(
        string questionId = "q1",
        string category = "General",
        int totalTokens = 10,
        double evalTps = 50,
        double promptTps = 100,
        List<TokenLogprob>? tokens = null,
        JudgmentResult? judgment = null)
    {
        tokens ??= CreateTokenLogprobs(count: totalTokens);

        return new QuestionResult
        {
            QuestionId = questionId,
            Category = category,
            Question = $"Question {questionId}",
            Answer = $"Answer {questionId}",
            Tokens = tokens,
            TotalTokens = totalTokens,
            EvalTokensPerSecond = evalTps,
            PromptTokensPerSecond = promptTps,
            Judgment = judgment
        };
    }

    public static QuantResult CreateQuantResult(
        string tag = "Q4_K_M",
        bool isBase = false,
        List<QuestionResult>? questions = null)
    {
        questions ??= new List<QuestionResult>
        {
            CreateQuestionResult(questionId: "q1", category: "General"),
            CreateQuestionResult(questionId: "q2", category: "Reasoning")
        };

        return new QuantResult
        {
            Tag = tag,
            ModelName = $"test-model:{tag}",
            DiskSizeBytes = isBase ? 8_000_000_000 : 4_000_000_000,
            Family = "llama",
            ParameterSize = "7B",
            QuantizationType = isBase ? "F16" : tag,
            IsBase = isBase,
            QuestionResults = questions
        };
    }

    public static QcResultsFile CreateQcResultsFile(
        QuantResult? baseResult = null,
        List<QuantResult>? quantResults = null)
    {
        baseResult ??= CreateQuantResult(tag: "F16", isBase: true);
        quantResults ??= new List<QuantResult>
        {
            CreateQuantResult(tag: "Q4_K_M")
        };

        var allResults = new List<QuantResult> { baseResult };
        allResults.AddRange(quantResults);

        return new QcResultsFile
        {
            TestSuiteName = "test-suite",
            ModelName = "test-model",
            Options = new QcTestOptions { Temperature = 0.0, Seed = 42 },
            Results = allResults
        };
    }

    public static JudgmentResult CreateJudgment(
        int score = 80,
        string bestAnswer = "B",
        string judgeModel = "gpt-4")
    {
        return new JudgmentResult
        {
            JudgeModel = judgeModel,
            Score = score,
            Reason = "Good quality answer",
            BestAnswer = bestAnswer,
            JudgedAt = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)
        };
    }
}
