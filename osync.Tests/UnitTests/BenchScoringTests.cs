using FluentAssertions;
using osync.Tests.Support;

namespace osync.Tests.UnitTests;

public class BenchScoringTests
{
    // ── CalculateOverallScore ───────────────────────────────────

    [Fact]
    public void CalculateOverallScore_EmptyCategories_ReturnsZero()
    {
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult>());
        BenchScoring.CalculateOverallScore(quant).Should().Be(0);
    }

    [Fact]
    public void CalculateOverallScore_NullCategories_ReturnsZero()
    {
        var quant = TestHelpers.CreateBenchQuantResult();
        quant.CategoryResults = null!;
        BenchScoring.CalculateOverallScore(quant).Should().Be(0);
    }

    [Fact]
    public void CalculateOverallScore_SingleCategory_ReturnsCategoryScore()
    {
        var cat = TestHelpers.CreateBenchCategory(score: 85, targetContextLength: 4096, totalQuestions: 3);
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult> { cat });

        BenchScoring.CalculateOverallScore(quant).Should().BeApproximately(85, 0.01);
    }

    [Fact]
    public void CalculateOverallScore_MultipleCategories_WeightedByLog2Context()
    {
        var cat1 = TestHelpers.CreateBenchCategory(score: 80, targetContextLength: 4096, totalQuestions: 3);
        var cat2 = TestHelpers.CreateBenchCategory(category: "Reasoning", score: 60, targetContextLength: 2048, totalQuestions: 3);
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult> { cat1, cat2 });

        // weight1 = Log2(4096) = 12, weight2 = Log2(2048) = 11
        // expected = (80*12 + 60*11) / (12+11) = (960+660)/23 = 70.43...
        var expected = (80.0 * 12 + 60.0 * 11) / 23.0;
        BenchScoring.CalculateOverallScore(quant).Should().BeApproximately(expected, 0.01);
    }

    [Fact]
    public void CalculateOverallScore_ZeroQuestionCategories_AreExcluded()
    {
        var cat1 = TestHelpers.CreateBenchCategory(score: 80, targetContextLength: 4096, totalQuestions: 3);
        var catEmpty = TestHelpers.CreateBenchCategory(category: "Empty", score: 0, targetContextLength: 2048, totalQuestions: 0);
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult> { cat1, catEmpty });

        BenchScoring.CalculateOverallScore(quant).Should().BeApproximately(80, 0.01);
    }

    // ── CalculateScoringResults ─────────────────────────────────

    [Fact]
    public void CalculateScoringResults_MapsMetadataCorrectly()
    {
        var file = TestHelpers.CreateBenchResultsFile();
        var result = BenchScoring.CalculateScoringResults(file);

        result.TestSuiteName.Should().Be("test-suite");
        result.ModelName.Should().Be("test-model");
        result.TotalQuants.Should().Be(2);
    }

    [Fact]
    public void CalculateScoringResults_SortsDescendingByOverallScore()
    {
        var q1 = TestHelpers.CreateBenchQuantResult(tag: "Q4_K_M", overallScore: 60);
        var q2 = TestHelpers.CreateBenchQuantResult(tag: "Q5_K_M", overallScore: 90);
        var file = TestHelpers.CreateBenchResultsFile(results: new List<BenchQuantResult> { q1, q2 });

        var result = BenchScoring.CalculateScoringResults(file);
        result.QuantScores[0].OverallScore.Should().Be(90);
        result.QuantScores[1].OverallScore.Should().Be(60);
    }

    [Fact]
    public void CalculateScoringResults_PopulatesCategoryScores()
    {
        var file = TestHelpers.CreateBenchResultsFile();
        var result = BenchScoring.CalculateScoringResults(file);

        result.QuantScores.Should().AllSatisfy(q =>
            q.CategoryScores.Should().NotBeEmpty());
    }

    [Fact]
    public void CalculateScoringResults_PopulatesSubCategoryScores()
    {
        var subs = new List<BenchSubCategoryResult>
        {
            TestHelpers.CreateBenchSubCategory(name: "Old", score: 70),
            TestHelpers.CreateBenchSubCategory(name: "New", score: 90)
        };
        var cat = TestHelpers.CreateBenchCategory(category: "Knowledge", subCategories: subs);
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult> { cat });
        var file = TestHelpers.CreateBenchResultsFile(results: new List<BenchQuantResult> { quant });

        var result = BenchScoring.CalculateScoringResults(file);
        result.QuantScores[0].SubCategoryScores.Should().ContainKey("Knowledge/Old");
        result.QuantScores[0].SubCategoryScores.Should().ContainKey("Knowledge/New");
        result.QuantScores[0].SubCategoryScores["Knowledge/Old"].Should().Be(70);
        result.QuantScores[0].SubCategoryScores["Knowledge/New"].Should().Be(90);
    }

    [Fact]
    public void CalculateScoringResults_PopulatesMinSpeeds()
    {
        var questions = new List<BenchQuestionResult>
        {
            TestHelpers.CreateBenchQuestion(id: 1, promptToksPerSec: 200, evalToksPerSec: 80),
            TestHelpers.CreateBenchQuestion(id: 2, promptToksPerSec: 100, evalToksPerSec: 50)
        };
        var cat = TestHelpers.CreateBenchCategory(questions: questions, totalQuestions: 2);
        var quant = TestHelpers.CreateBenchQuantResult(categories: new List<BenchCategoryResult> { cat });
        var file = TestHelpers.CreateBenchResultsFile(results: new List<BenchQuantResult> { quant });

        var result = BenchScoring.CalculateScoringResults(file);
        var qs = result.QuantScores[0];
        qs.MinPromptToksPerSec["General Knowledge"].Should().Be(100);
        qs.MinEvalToksPerSec["General Knowledge"].Should().Be(50);
    }

    [Fact]
    public void CalculateScoringResults_CountsCategories()
    {
        var file = TestHelpers.CreateBenchResultsFile();
        var result = BenchScoring.CalculateScoringResults(file);

        // Default fixture has "General Knowledge" and "Reasoning"
        result.TotalCategories.Should().Be(2);
    }

    // ── FormatSize ──────────────────────────────────────────────

    [Fact]
    public void FormatSize_KB_FormatsCorrectly()
    {
        BenchScoring.FormatSize(500 * 1024).Should().Contain("KB");
    }

    [Fact]
    public void FormatSize_MB_FormatsCorrectly()
    {
        var result = BenchScoring.FormatSize(1024 * 1024);
        result.Should().Contain("MB");
    }

    [Fact]
    public void FormatSize_GB_FormatsCorrectly()
    {
        var result = BenchScoring.FormatSize(1024L * 1024 * 1024);
        result.Should().Contain("GB");
    }

    // ── FormatSpeed ─────────────────────────────────────────────

    [Fact]
    public void FormatSpeed_BelowThousand_NoSuffix()
    {
        BenchScoring.FormatSpeed(500).Should().Be("500");
    }

    [Fact]
    public void FormatSpeed_AboveThousand_HasKSuffix()
    {
        BenchScoring.FormatSpeed(1300).Should().EndWith("k");
    }

    // ── FormatResponseTime ──────────────────────────────────────

    [Fact]
    public void FormatResponseTime_UnderMinute_HasSecondsSuffix()
    {
        BenchScoring.FormatResponseTime(1500).Should().EndWith("s");
        BenchScoring.FormatResponseTime(1500).Should().NotContain(":");
    }

    [Fact]
    public void FormatResponseTime_OverMinute_HasColonFormat()
    {
        BenchScoring.FormatResponseTime(90000).Should().Contain(":");
    }

    // ── GetCategoryStatistics ───────────────────────────────────

    [Fact]
    public void GetCategoryStatistics_CalculatesCorrectly()
    {
        var q1 = TestHelpers.CreateBenchQuantResult(tag: "Q4");
        var q2 = TestHelpers.CreateBenchQuantResult(tag: "Q5");
        // Both have "General Knowledge" at score 80 by default
        var file = TestHelpers.CreateBenchResultsFile(results: new List<BenchQuantResult> { q1, q2 });
        var scoring = BenchScoring.CalculateScoringResults(file);

        var stats = BenchScoring.GetCategoryStatistics(scoring);
        stats.Should().ContainKey("General Knowledge");
        stats["General Knowledge"].Scores.Should().HaveCount(2);
        stats["General Knowledge"].Average.Should().Be(80);
        stats["General Knowledge"].Min.Should().Be(80);
        stats["General Knowledge"].Max.Should().Be(80);
        stats["General Knowledge"].StdDev.Should().Be(0); // identical values
    }

    [Fact]
    public void GetCategoryStatistics_SingleScore_StdDevZero()
    {
        var quant = TestHelpers.CreateBenchQuantResult();
        var file = TestHelpers.CreateBenchResultsFile(results: new List<BenchQuantResult> { quant });
        var scoring = BenchScoring.CalculateScoringResults(file);

        var stats = BenchScoring.GetCategoryStatistics(scoring);
        foreach (var stat in stats.Values)
        {
            stat.StdDev.Should().Be(0);
        }
    }

    [Fact]
    public void GetCategoryStatistics_EmptyScoring_ReturnsEmpty()
    {
        var scoring = new BenchScoringResults();
        var stats = BenchScoring.GetCategoryStatistics(scoring);
        stats.Should().BeEmpty();
    }

    // ── GetRank ─────────────────────────────────────────────────

    [Fact]
    public void GetRank_HighestScore_ReturnsOne()
    {
        var scores = new List<double> { 90, 80, 70 };
        BenchScoring.GetRank(90, scores).Should().Be(1);
    }

    [Fact]
    public void GetRank_MiddleScore_ReturnsCorrectRank()
    {
        var scores = new List<double> { 90, 80, 70 };
        BenchScoring.GetRank(80, scores).Should().Be(2);
    }

    [Fact]
    public void GetRank_LowestScore_ReturnsLast()
    {
        var scores = new List<double> { 90, 80, 70 };
        BenchScoring.GetRank(70, scores).Should().Be(3);
    }

    // ── CalculateDelta ──────────────────────────────────────────

    [Theory]
    [InlineData(85, 80, 5)]
    [InlineData(70, 80, -10)]
    [InlineData(80, 80, 0)]
    public void CalculateDelta_ReturnsCorrectDifference(double current, double baseline, double expected)
    {
        BenchScoring.CalculateDelta(current, baseline).Should().Be(expected);
    }

    // ── GetScoreRating ──────────────────────────────────────────

    [Theory]
    [InlineData(100, "Excellent")]
    [InlineData(95, "Excellent")]
    [InlineData(94.9, "Very Good")]
    [InlineData(85, "Very Good")]
    [InlineData(84.9, "Good")]
    [InlineData(75, "Good")]
    [InlineData(74.9, "Fair")]
    [InlineData(60, "Fair")]
    [InlineData(59.9, "Poor")]
    [InlineData(40, "Poor")]
    [InlineData(39.9, "Very Poor")]
    [InlineData(0, "Very Poor")]
    public void GetScoreRating_ReturnCorrectLabel(double score, string expected)
    {
        BenchScoring.GetScoreRating(score).Should().Be(expected);
    }

    // ── GetScoreColor ───────────────────────────────────────────

    [Theory]
    [InlineData(95, "green")]
    [InlineData(85, "lime")]
    [InlineData(75, "yellow")]
    [InlineData(60, "orange1")]
    [InlineData(59, "red")]
    [InlineData(0, "red")]
    public void GetScoreColor_ReturnsCorrectColor(double score, string expected)
    {
        BenchScoring.GetScoreColor(score).Should().Be(expected);
    }
}
