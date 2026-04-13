using FluentAssertions;
using osync.Tests.Support;

namespace osync.Tests.UnitTests;

public class QcScoringTests
{
    // ── Basic validation ────────────────────────────────────────

    [Fact]
    public void CalculateScores_NoBaseResult_ThrowsInvalidOperation()
    {
        var file = new QcResultsFile
        {
            TestSuiteName = "test",
            ModelName = "model",
            Options = new QcTestOptions(),
            Results = new List<QuantResult>
            {
                TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false)
            }
        };

        var act = () => QcScoring.CalculateScores(file);
        act.Should().Throw<InvalidOperationException>().WithMessage("*base*");
    }

    [Fact]
    public void CalculateScores_OnlyBaseResult_ReturnsEmptyQuantScores()
    {
        var file = TestHelpers.CreateQcResultsFile(quantResults: new List<QuantResult>());
        var result = QcScoring.CalculateScores(file);

        result.QuantScores.Should().BeEmpty();
        result.BaseTag.Should().Be("F16");
    }

    // ── Metadata mapping ────────────────────────────────────────

    [Fact]
    public void CalculateScores_MapsBaseMetadataCorrectly()
    {
        var file = TestHelpers.CreateQcResultsFile();
        var result = QcScoring.CalculateScores(file);

        result.BaseModelName.Should().Be("test-model");
        result.BaseTag.Should().Be("F16");
        result.BaseFamily.Should().Be("llama");
        result.BaseParameterSize.Should().Be("7B");
        result.TestSuiteName.Should().Be("test-suite");
    }

    // ── Identical base and quant ────────────────────────────────

    [Fact]
    public void CalculateScores_IdenticalTokensAndLogprobs_HighConfidenceScore()
    {
        // Create base and quant with identical tokens/logprobs
        var tokens = TestHelpers.CreateTokenLogprobs(count: 10, avgLogprob: -0.5);
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", tokens: new List<TokenLogprob>(tokens.Select(t => new TokenLogprob { Token = t.Token, Logprob = t.Logprob }))),
            TestHelpers.CreateQuestionResult(questionId: "q2", tokens: new List<TokenLogprob>(tokens.Select(t => new TokenLogprob { Token = t.Token, Logprob = t.Logprob })))
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", tokens: new List<TokenLogprob>(tokens.Select(t => new TokenLogprob { Token = t.Token, Logprob = t.Logprob }))),
            TestHelpers.CreateQuestionResult(questionId: "q2", tokens: new List<TokenLogprob>(tokens.Select(t => new TokenLogprob { Token = t.Token, Logprob = t.Logprob })))
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        result.QuantScores.Should().HaveCount(1);
        // Identical data should produce very high confidence
        result.QuantScores[0].TotalConfidenceScore.Should().BeGreaterThan(90);
    }

    // ── Very different tokens ───────────────────────────────────

    [Fact]
    public void CalculateScores_VeryDifferentTokens_LowConfidenceScore()
    {
        var baseTokens = Enumerable.Range(0, 10)
            .Select(i => new TokenLogprob { Token = $"base{i}", Logprob = -0.5 })
            .ToList();
        var quantTokens = Enumerable.Range(0, 10)
            .Select(i => new TokenLogprob { Token = $"quant{i}", Logprob = -5.0 })
            .ToList();

        var baseQ = TestHelpers.CreateQuestionResult(questionId: "q1", tokens: baseTokens);
        var quantQ = TestHelpers.CreateQuestionResult(questionId: "q1", tokens: quantTokens);

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: new List<QuestionResult> { baseQ });
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q2_K", isBase: false, questions: new List<QuestionResult> { quantQ });
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        // Very different tokens and logprobs → low score
        result.QuantScores[0].TotalConfidenceScore.Should().BeLessThan(50);
    }

    // ── Weighted formula verification ───────────────────────────

    [Fact]
    public void CalculateScores_WeightedFormulaApplied()
    {
        // The 4-component formula: 70% logprobs + 20% perplexity + 5% token sim + 5% length
        var file = TestHelpers.CreateQcResultsFile();
        var result = QcScoring.CalculateScores(file);

        var qs = result.QuantScores[0].QuestionScores!;
        foreach (var q in qs)
        {
            var expected =
                q.TokenSimilarityScore * 0.05 +
                q.LogprobsDivergenceScore * 0.70 +
                q.LengthConsistencyScore * 0.05 +
                q.PerplexityScore * 0.20;

            q.OverallConfidenceScore.Should().BeApproximately(expected, 0.01);
        }
    }

    // ── FinalScore without judgment ─────────────────────────────

    [Fact]
    public void CalculateScores_NoJudgment_FinalScoreEqualsMetrics()
    {
        var file = TestHelpers.CreateQcResultsFile();
        var result = QcScoring.CalculateScores(file);

        foreach (var q in result.QuantScores)
        {
            q.HasJudgmentScoring.Should().BeFalse();
            q.FinalScore.Should().Be(q.TotalConfidenceScore);
        }
    }

    // ── FinalScore with judgment ────────────────────────────────

    [Fact]
    public void CalculateScores_WithJudgment_FinalScore50_50()
    {
        var judgment = TestHelpers.CreateJudgment(score: 90, bestAnswer: "B");
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1"),
            TestHelpers.CreateQuestionResult(questionId: "q2")
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", judgment: TestHelpers.CreateJudgment(score: 90, bestAnswer: "B")),
            TestHelpers.CreateQuestionResult(questionId: "q2", judgment: TestHelpers.CreateJudgment(score: 80, bestAnswer: "A"))
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        var qs = result.QuantScores[0];

        qs.HasJudgmentScoring.Should().BeTrue();
        qs.AverageJudgmentScore.Should().Be(85); // (90+80)/2
        var expectedFinal = (qs.TotalConfidenceScore * 0.50) + (85.0 * 0.50);
        qs.FinalScore.Should().BeApproximately(expectedFinal, 0.01);
    }

    // ── Best answer counting ────────────────────────────────────

    [Fact]
    public void CalculateScores_BestAnswerCounting_Correct()
    {
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1"),
            TestHelpers.CreateQuestionResult(questionId: "q2"),
            TestHelpers.CreateQuestionResult(questionId: "q3")
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", judgment: TestHelpers.CreateJudgment(score: 90, bestAnswer: "B")),
            TestHelpers.CreateQuestionResult(questionId: "q2", judgment: TestHelpers.CreateJudgment(score: 70, bestAnswer: "A")),
            TestHelpers.CreateQuestionResult(questionId: "q3", judgment: TestHelpers.CreateJudgment(score: 85, bestAnswer: "AB"))
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        var qs = result.QuantScores[0];

        qs.BestCount.Should().Be(1);    // B
        qs.WorstCount.Should().Be(1);   // A
        qs.TieCount.Should().Be(1);     // AB
    }

    // ── Performance metrics ─────────────────────────────────────

    [Fact]
    public void CalculateScores_PerformanceMetrics_CalculatedCorrectly()
    {
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", evalTps: 50, promptTps: 200)
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", evalTps: 75, promptTps: 150)
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        var qs = result.QuantScores[0];

        qs.EvalTokensPerSecond.Should().Be(75);
        qs.PromptTokensPerSecond.Should().Be(150);
        qs.EvalPerformancePercent.Should().BeApproximately(150, 0.01); // 75/50 * 100
        qs.PromptPerformancePercent.Should().BeApproximately(75, 0.01); // 150/200 * 100
    }

    // ── Edge cases ──────────────────────────────────────────────

    [Fact]
    public void CalculateScores_EmptyTokens_ScoresZero()
    {
        var baseQ = TestHelpers.CreateQuestionResult(questionId: "q1", tokens: new List<TokenLogprob>(), totalTokens: 0);
        var quantQ = TestHelpers.CreateQuestionResult(questionId: "q1", tokens: new List<TokenLogprob>(), totalTokens: 0);

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: new List<QuestionResult> { baseQ });
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: new List<QuestionResult> { quantQ });
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        result.QuantScores[0].TotalConfidenceScore.Should().Be(0);
    }

    [Fact]
    public void CalculateScores_MismatchedQuestionIds_SkipsMissing()
    {
        var baseQ = TestHelpers.CreateQuestionResult(questionId: "q1");
        var quantQ = TestHelpers.CreateQuestionResult(questionId: "q999"); // no match

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: new List<QuestionResult> { baseQ });
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: new List<QuestionResult> { quantQ });
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        result.QuantScores[0].QuestionScores.Should().BeEmpty();
        result.QuantScores[0].TotalConfidenceScore.Should().Be(0);
    }

    [Fact]
    public void CalculateScores_CategoryScores_AveragedPerCategory()
    {
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", category: "Math"),
            TestHelpers.CreateQuestionResult(questionId: "q2", category: "Math"),
            TestHelpers.CreateQuestionResult(questionId: "q3", category: "English")
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", category: "Math"),
            TestHelpers.CreateQuestionResult(questionId: "q2", category: "Math"),
            TestHelpers.CreateQuestionResult(questionId: "q3", category: "English")
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        var qs = result.QuantScores[0];

        qs.CategoryScores.Should().ContainKey("Math");
        qs.CategoryScores.Should().ContainKey("English");
        // Math has 2 questions, English has 1
        qs.CategoryScores["Math"].Should().BeGreaterThan(0);
        qs.CategoryScores["English"].Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateScores_HasJudgmentScoring_OnlyWhenAllQuestionsJudged()
    {
        // One question judged, one not → HasJudgmentScoring = false
        var baseQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1"),
            TestHelpers.CreateQuestionResult(questionId: "q2")
        };
        var quantQuestions = new List<QuestionResult>
        {
            TestHelpers.CreateQuestionResult(questionId: "q1", judgment: TestHelpers.CreateJudgment()),
            TestHelpers.CreateQuestionResult(questionId: "q2") // no judgment
        };

        var baseResult = TestHelpers.CreateQuantResult(tag: "F16", isBase: true, questions: baseQuestions);
        var quantResult = TestHelpers.CreateQuantResult(tag: "Q4_K_M", isBase: false, questions: quantQuestions);
        var file = TestHelpers.CreateQcResultsFile(baseResult: baseResult, quantResults: new List<QuantResult> { quantResult });

        var result = QcScoring.CalculateScores(file);
        result.QuantScores[0].HasJudgmentScoring.Should().BeFalse();
        result.QuantScores[0].FinalScore.Should().Be(result.QuantScores[0].TotalConfidenceScore);
    }
}
