namespace osync
{
    /// <summary>
    /// Scoring algorithms for comparing quantizations against base model
    /// Implements 4-component weighted scoring system with optional judgment scoring
    /// </summary>
    public class QcScoring
    {
        // Scoring weights for metrics (must sum to 100%)
        private const double LOGPROBS_DIVERGENCE_WEIGHT = 0.70;
        private const double PERPLEXITY_WEIGHT = 0.20;
        private const double TOKEN_SIMILARITY_WEIGHT = 0.05;
        private const double LENGTH_CONSISTENCY_WEIGHT = 0.05;

        // Final score weights when judgment is available
        private const double METRICS_WEIGHT = 0.50;
        private const double JUDGMENT_WEIGHT = 0.50;

        /// <summary>
        /// Calculate comprehensive scores for all quantizations compared to base
        /// </summary>
        public static QcScoringResults CalculateScores(QcResultsFile resultsFile)
        {
            var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);
            if (baseResult == null)
                throw new InvalidOperationException("No base quantization found in results");

            // Calculate base model performance metrics
            var baseEvalTps = baseResult.QuestionResults
                .Where(q => q.EvalTokensPerSecond > 0)
                .Select(q => q.EvalTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            var basePromptTps = baseResult.QuestionResults
                .Where(q => q.PromptTokensPerSecond > 0)
                .Select(q => q.PromptTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            var scoringResults = new QcScoringResults
            {
                BaseModelName = resultsFile.ModelName,
                BaseTag = baseResult.Tag,
                BaseFamily = baseResult.Family,
                BaseParameterSize = baseResult.ParameterSize,
                BaseDiskSizeBytes = baseResult.DiskSizeBytes,
                BaseQuantizationType = baseResult.QuantizationType,
                BaseEvalTokensPerSecond = baseEvalTps,
                BasePromptTokensPerSecond = basePromptTps,
                Options = resultsFile.Options,
                TestSuiteName = resultsFile.TestSuiteName,
                TotalQuestions = baseResult.QuestionResults.Count
            };

            // Calculate scores for each quantization (excluding base)
            foreach (var quantResult in resultsFile.Results.Where(r => !r.IsBase))
            {
                var scoreResult = CalculateQuantizationScore(baseResult, quantResult);
                scoringResults.QuantScores.Add(scoreResult);
            }

            // Check if all quantizations have judgment scoring
            var allHaveJudgment = scoringResults.QuantScores.All(q => q.HasJudgmentScoring);
            scoringResults.HasJudgmentScoring = allHaveJudgment;

            // Get judge model name from first quantization that has judgment
            if (allHaveJudgment && resultsFile.Results.Any(r => !r.IsBase))
            {
                var firstQuant = resultsFile.Results.FirstOrDefault(r => !r.IsBase);
                var firstQuestion = firstQuant?.QuestionResults.FirstOrDefault(q => q.Judgment != null);
                scoringResults.JudgeModel = firstQuestion?.Judgment?.JudgeModel;
            }

            return scoringResults;
        }

        /// <summary>
        /// Calculate scores for a single quantization compared to base
        /// </summary>
        private static QuantScoreResult CalculateQuantizationScore(QuantResult baseResult, QuantResult quantResult)
        {
            var scoreResult = new QuantScoreResult
            {
                Tag = quantResult.Tag,
                DiskSizeBytes = quantResult.DiskSizeBytes,
                QuantizationType = quantResult.QuantizationType,
                QuestionScores = new List<QuestionScore>()
            };

            // Calculate per-question scores
            var categoryScores = new Dictionary<string, List<double>>();
            var categoryJudgmentScores = new Dictionary<string, List<double>>();
            double totalConfidence = 0;
            double totalJudgment = 0;
            int questionCount = 0;
            int judgmentCount = 0;

            foreach (var baseQuestion in baseResult.QuestionResults)
            {
                var quantQuestion = quantResult.QuestionResults
                    .FirstOrDefault(q => q.QuestionId == baseQuestion.QuestionId);

                if (quantQuestion == null)
                    continue;

                var questionScore = CalculateQuestionScore(baseQuestion, quantQuestion);
                scoreResult.QuestionScores.Add(questionScore);

                // Accumulate category scores (metrics)
                if (!categoryScores.ContainsKey(questionScore.Category))
                    categoryScores[questionScore.Category] = new List<double>();

                categoryScores[questionScore.Category].Add(questionScore.OverallConfidenceScore);
                totalConfidence += questionScore.OverallConfidenceScore;
                questionCount++;

                // Accumulate judgment scores if available (both total and per-category)
                if (questionScore.JudgmentScore.HasValue)
                {
                    totalJudgment += questionScore.JudgmentScore.Value;
                    judgmentCount++;

                    // Accumulate per-category judgment scores
                    if (!categoryJudgmentScores.ContainsKey(questionScore.Category))
                        categoryJudgmentScores[questionScore.Category] = new List<double>();

                    categoryJudgmentScores[questionScore.Category].Add(questionScore.JudgmentScore.Value);
                }
            }

            // Calculate category averages (metrics)
            foreach (var category in categoryScores)
            {
                scoreResult.CategoryScores[category.Key] = category.Value.Average();
            }

            // Calculate category averages (judgment)
            foreach (var category in categoryJudgmentScores)
            {
                scoreResult.CategoryJudgmentScores[category.Key] = category.Value.Average();
            }

            // Calculate overall confidence score (metrics only)
            scoreResult.TotalConfidenceScore = questionCount > 0 ? totalConfidence / questionCount : 0;

            // Calculate judgment score if all questions have judgment
            if (judgmentCount == questionCount && questionCount > 0)
            {
                scoreResult.HasJudgmentScoring = true;
                scoreResult.AverageJudgmentScore = totalJudgment / judgmentCount;

                // Final score: 50% metrics + 50% judgment
                scoreResult.FinalScore = (scoreResult.TotalConfidenceScore * METRICS_WEIGHT) +
                                        (scoreResult.AverageJudgmentScore.Value * JUDGMENT_WEIGHT);
            }
            else
            {
                // No judgment - final score equals metrics score
                scoreResult.HasJudgmentScoring = false;
                scoreResult.FinalScore = scoreResult.TotalConfidenceScore;
            }

            // Calculate performance metrics
            CalculatePerformanceMetrics(baseResult, quantResult, scoreResult);

            return scoreResult;
        }

        /// <summary>
        /// Calculate score for a single question comparison
        /// </summary>
        private static QuestionScore CalculateQuestionScore(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            var score = new QuestionScore
            {
                QuestionId = baseQuestion.QuestionId,
                Category = baseQuestion.Category
            };

            // 1. Token Sequence Similarity (5%)
            score.TokenSimilarityScore = CalculateTokenSimilarity(baseQuestion, quantQuestion);

            // 2. Logprobs Divergence Score (70%)
            score.LogprobsDivergenceScore = CalculateLogprobsDivergence(baseQuestion, quantQuestion);

            // 3. Answer Length Consistency (5%)
            score.LengthConsistencyScore = CalculateLengthConsistency(baseQuestion, quantQuestion);

            // 4. Perplexity Score (20%)
            score.PerplexityScore = CalculatePerplexityScore(baseQuestion, quantQuestion);

            // Calculate weighted overall score (0-100%)
            score.OverallConfidenceScore =
                (score.TokenSimilarityScore * TOKEN_SIMILARITY_WEIGHT) +
                (score.LogprobsDivergenceScore * LOGPROBS_DIVERGENCE_WEIGHT) +
                (score.LengthConsistencyScore * LENGTH_CONSISTENCY_WEIGHT) +
                (score.PerplexityScore * PERPLEXITY_WEIGHT);

            // Include judgment score if available
            if (quantQuestion.Judgment != null)
            {
                score.JudgmentScore = quantQuestion.Judgment.Score;
            }

            return score;
        }

        /// <summary>
        /// Calculate token sequence similarity (0-100%)
        /// Uses Longest Common Subsequence (LCS) approach
        /// More forgiving of small variations while still detecting major differences
        /// </summary>
        private static double CalculateTokenSimilarity(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (baseQuestion.Tokens.Count == 0 || quantQuestion.Tokens.Count == 0)
                return 0;

            // Calculate LCS (Longest Common Subsequence) length
            int lcsLength = CalculateLCS(
                baseQuestion.Tokens.Select(t => t.Token).ToList(),
                quantQuestion.Tokens.Select(t => t.Token).ToList()
            );

            // Use average length as denominator for balanced scoring
            double avgLength = (baseQuestion.Tokens.Count + quantQuestion.Tokens.Count) / 2.0;

            // LCS ratio gives a more forgiving similarity measure
            // Exact match: 100%, substantial overlap: 70-99%, some overlap: 40-69%, little overlap: <40%
            double similarity = (lcsLength / avgLength) * 100.0;

            return Math.Min(100.0, similarity);
        }

        /// <summary>
        /// Calculate Longest Common Subsequence length using dynamic programming
        /// </summary>
        private static int CalculateLCS(List<string> seq1, List<string> seq2)
        {
            int m = seq1.Count;
            int n = seq2.Count;
            int[,] dp = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (seq1[i - 1] == seq2[j - 1])
                        dp[i, j] = dp[i - 1, j - 1] + 1;
                    else
                        dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
                }
            }

            return dp[m, n];
        }

        /// <summary>
        /// Calculate logprobs divergence score (0-100%)
        /// Compares sequence-level confidence between base and quantization
        /// High scores indicate both models are similarly confident in their choices
        /// </summary>
        private static double CalculateLogprobsDivergence(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (baseQuestion.Tokens.Count == 0 || quantQuestion.Tokens.Count == 0)
                return 0;

            // Calculate average confidence (mean logprob) for each sequence
            // Higher (closer to 0) logprobs = more confident
            double baseAvgLogprob = baseQuestion.Tokens.Average(t => t.Logprob);
            double quantAvgLogprob = quantQuestion.Tokens.Average(t => t.Logprob);

            // Calculate confidence scores (normalize logprobs to 0-1 range)
            // Typical logprobs range: -10 (very uncertain) to 0 (very confident)
            // We use exp(logprob) to convert to probability space
            double baseConfidence = Math.Exp(baseAvgLogprob);
            double quantConfidence = Math.Exp(quantAvgLogprob);

            // Calculate relative confidence difference
            // If both models are confident (close to 1.0), difference is small
            // If confidences diverge, score decreases
            double confidenceDiff = Math.Abs(baseConfidence - quantConfidence);

            // Convert to similarity score (0-100%)
            // Small difference = high score, large difference = low score
            // Using exponential decay: 0.0 diff = 100%, 0.1 diff ≈ 82%, 0.2 diff ≈ 67%, 0.5 diff ≈ 22%
            double score = 100.0 * Math.Exp(-confidenceDiff * 2.0);

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// Calculate answer length consistency (0-100%)
        /// Measures how close the answer length is to base
        /// </summary>
        private static double CalculateLengthConsistency(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (baseQuestion.TotalTokens == 0)
                return 0;

            double lengthRatio = (double)quantQuestion.TotalTokens / baseQuestion.TotalTokens;

            // Perfect match (ratio = 1.0) gets 100%
            // Ratio deviating from 1.0 loses points exponentially
            double deviation = Math.Abs(1.0 - lengthRatio);
            double score = 100.0 * Math.Exp(-deviation * 2.0);

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// Calculate perplexity-based score (0-100%)
        /// Lower perplexity = better model confidence
        /// Compares perplexity of quantization vs base
        /// </summary>
        private static double CalculatePerplexityScore(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (baseQuestion.Tokens.Count == 0 || quantQuestion.Tokens.Count == 0)
                return 0;

            // Calculate perplexity for base
            double basePerplexity = CalculatePerplexity(baseQuestion.Tokens);
            double quantPerplexity = CalculatePerplexity(quantQuestion.Tokens);

            if (basePerplexity == 0)
                return 0;

            // Compare perplexities
            // If quant perplexity is close to base, score is high
            double perplexityRatio = quantPerplexity / basePerplexity;

            // Perfect match (ratio = 1.0) gets 100%
            // Using gentler decay since perplexity can vary more for similar models
            // Scale factor 0.5 means: ratio 1.0 = 100%, 1.5 ≈ 78%, 2.0 ≈ 61%, 3.0 ≈ 37%
            double deviation = Math.Abs(1.0 - perplexityRatio);
            double score = 100.0 * Math.Exp(-deviation * 0.5);

            return Math.Max(0, Math.Min(100, score));
        }

        /// <summary>
        /// Calculate perplexity from token logprobs
        /// Perplexity = exp(average negative log probability)
        /// </summary>
        private static double CalculatePerplexity(List<TokenLogprob> tokens)
        {
            if (tokens.Count == 0)
                return 0;

            double sumLogprobs = tokens.Sum(t => t.Logprob);
            double avgLogprob = sumLogprobs / tokens.Count;

            return Math.Exp(-avgLogprob);
        }

        /// <summary>
        /// Calculate performance metrics (eval/prompt tokens per second)
        /// Compare quantization speed to base speed
        /// </summary>
        private static void CalculatePerformanceMetrics(QuantResult baseResult, QuantResult quantResult, QuantScoreResult scoreResult)
        {
            // Calculate average tokens per second across all questions
            var baseEvalTps = baseResult.QuestionResults
                .Where(q => q.EvalTokensPerSecond > 0)
                .Select(q => q.EvalTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            var basePromptTps = baseResult.QuestionResults
                .Where(q => q.PromptTokensPerSecond > 0)
                .Select(q => q.PromptTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            var quantEvalTps = quantResult.QuestionResults
                .Where(q => q.EvalTokensPerSecond > 0)
                .Select(q => q.EvalTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            var quantPromptTps = quantResult.QuestionResults
                .Where(q => q.PromptTokensPerSecond > 0)
                .Select(q => q.PromptTokensPerSecond)
                .DefaultIfEmpty(0)
                .Average();

            scoreResult.EvalTokensPerSecond = quantEvalTps;
            scoreResult.PromptTokensPerSecond = quantPromptTps;

            // Calculate performance percentages (quant speed vs base speed)
            if (baseEvalTps > 0)
                scoreResult.EvalPerformancePercent = (quantEvalTps / baseEvalTps) * 100.0;

            if (basePromptTps > 0)
                scoreResult.PromptPerformancePercent = (quantPromptTps / basePromptTps) * 100.0;
        }
    }
}
