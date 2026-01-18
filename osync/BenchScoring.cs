namespace osync
{
    /// <summary>
    /// Score calculation utilities for benchmark results.
    /// </summary>
    public static class BenchScoring
    {
        /// <summary>
        /// Calculate the overall score for a quantization result.
        /// Score is weighted average across categories, giving more weight to larger contexts.
        /// </summary>
        public static double CalculateOverallScore(BenchQuantResult quantResult)
        {
            if (quantResult.CategoryResults == null || quantResult.CategoryResults.Count == 0)
                return 0;

            // Use weighted average where weight = category context length
            double weightedSum = 0;
            double totalWeight = 0;

            foreach (var category in quantResult.CategoryResults)
            {
                if (category.TotalQuestions > 0)
                {
                    var weight = Math.Log2(category.TargetContextLength);
                    weightedSum += category.Score * weight;
                    totalWeight += weight;
                }
            }

            return totalWeight > 0 ? weightedSum / totalWeight : 0;
        }

        /// <summary>
        /// Calculate scores from a results file for display purposes.
        /// </summary>
        public static BenchScoringResults CalculateScoringResults(BenchResultsFile resultsFile)
        {
            var scoring = new BenchScoringResults
            {
                TestSuiteName = resultsFile.TestSuiteName,
                TestType = resultsFile.TestType,
                TestDescription = resultsFile.TestDescription,
                ModelName = resultsFile.ModelName,
                JudgeModel = resultsFile.JudgeModel,
                JudgeProvider = resultsFile.JudgeProvider,
                JudgeApiVersion = resultsFile.JudgeApiVersion,
                Options = resultsFile.Options,
                TestedAt = resultsFile.TestedAt,
                OsyncVersion = resultsFile.OsyncVersion,
                OllamaVersion = resultsFile.OllamaVersion,
                MaxContextLength = resultsFile.MaxContextLength,
                CategoryLimit = resultsFile.CategoryLimit,
                TotalQuants = resultsFile.Results.Count
            };

            // Get all unique categories
            var allCategories = resultsFile.Results
                .SelectMany(r => r.CategoryResults?.Select(c => c.Category) ?? Enumerable.Empty<string>())
                .Distinct()
                .ToList();

            scoring.TotalCategories = allCategories.Count;

            // Calculate per-quant scores
            foreach (var quantResult in resultsFile.Results)
            {
                var quantScore = new BenchQuantScoreResult
                {
                    Tag = quantResult.Tag,
                    ModelName = quantResult.ModelName,
                    RepositoryUrl = quantResult.RepositoryUrl,
                    DiskSizeBytes = quantResult.DiskSizeBytes,
                    ParameterSize = quantResult.ParameterSize,
                    QuantizationType = quantResult.QuantizationType,
                    EnhancedQuantization = quantResult.EnhancedQuantization,
                    OverallScore = quantResult.OverallScore,
                    TotalQuestions = quantResult.TotalQuestions,
                    CorrectAnswers = quantResult.CorrectAnswers
                };

                // Calculate per-category scores
                if (quantResult.CategoryResults != null)
                {
                    foreach (var catResult in quantResult.CategoryResults)
                    {
                        quantScore.CategoryScores[catResult.Category] = catResult.Score;

                        if (catResult.ContextTokensUsed.HasValue)
                            quantScore.ContextTokensUsed[catResult.Category] = catResult.ContextTokensUsed.Value;

                        // Calculate min speeds and avg response time from question results
                        var allQuestions = GetAllCategoryQuestions(catResult);
                        if (allQuestions.Count > 0)
                        {
                            var promptSpeeds = allQuestions
                                .Where(q => q.PromptToksPerSec.HasValue && q.PromptToksPerSec.Value > 0)
                                .Select(q => q.PromptToksPerSec!.Value)
                                .ToList();
                            var evalSpeeds = allQuestions
                                .Where(q => q.EvalToksPerSec.HasValue && q.EvalToksPerSec.Value > 0)
                                .Select(q => q.EvalToksPerSec!.Value)
                                .ToList();

                            if (promptSpeeds.Count > 0)
                                quantScore.MinPromptToksPerSec[catResult.Category] = promptSpeeds.Min();
                            if (evalSpeeds.Count > 0)
                                quantScore.MinEvalToksPerSec[catResult.Category] = evalSpeeds.Min();
                        }

                        // Get average response time from category result
                        if (catResult.AvgResponseTimeMs.HasValue)
                            quantScore.AvgResponseTimeMs[catResult.Category] = catResult.AvgResponseTimeMs.Value;

                        // Calculate subcategory scores if present
                        if (catResult.SubCategoryResults != null)
                        {
                            foreach (var subResult in catResult.SubCategoryResults)
                            {
                                var key = $"{catResult.Category}/{subResult.SubCategory}";
                                quantScore.SubCategoryScores[key] = subResult.Score;
                            }
                        }
                    }
                }

                scoring.QuantScores.Add(quantScore);
            }

            // Sort by overall score descending
            scoring.QuantScores = scoring.QuantScores
                .OrderByDescending(q => q.OverallScore)
                .ToList();

            return scoring;
        }

        /// <summary>
        /// Get a formatted size string from bytes.
        /// </summary>
        public static string FormatSize(long bytes)
        {
            const long GB = 1024 * 1024 * 1024;
            const long MB = 1024 * 1024;

            if (bytes >= GB)
                return $"{bytes / (double)GB:F2} GB";
            if (bytes >= MB)
                return $"{bytes / (double)MB:F1} MB";
            return $"{bytes / 1024:F0} KB";
        }

        /// <summary>
        /// Format speed with k suffix (e.g., 1300 -> "1.3k", 500 -> "500").
        /// </summary>
        public static string FormatSpeed(double speed)
        {
            if (speed >= 1000)
                return $"{speed / 1000:F1}k";
            return $"{speed:F0}";
        }

        /// <summary>
        /// Format time in milliseconds to seconds or minutes:seconds.
        /// </summary>
        public static string FormatResponseTime(double ms)
        {
            var seconds = ms / 1000.0;
            if (seconds >= 60)
            {
                var minutes = (int)(seconds / 60);
                var remainingSeconds = seconds % 60;
                return $"{minutes}:{remainingSeconds:F0}s";
            }
            return $"{seconds:F1}s";
        }

        /// <summary>
        /// Calculate category-level statistics across all quants.
        /// </summary>
        public static Dictionary<string, CategoryStats> GetCategoryStatistics(BenchScoringResults scoring)
        {
            var stats = new Dictionary<string, CategoryStats>();

            foreach (var quant in scoring.QuantScores)
            {
                foreach (var kvp in quant.CategoryScores)
                {
                    if (!stats.ContainsKey(kvp.Key))
                    {
                        stats[kvp.Key] = new CategoryStats { Category = kvp.Key };
                    }

                    stats[kvp.Key].Scores.Add(kvp.Value);
                }
            }

            // Calculate statistics
            foreach (var stat in stats.Values)
            {
                if (stat.Scores.Count > 0)
                {
                    stat.Average = stat.Scores.Average();
                    stat.Min = stat.Scores.Min();
                    stat.Max = stat.Scores.Max();
                    stat.StdDev = CalculateStdDev(stat.Scores);
                }
            }

            return stats;
        }

        /// <summary>
        /// Calculate standard deviation.
        /// </summary>
        private static double CalculateStdDev(List<double> values)
        {
            if (values.Count < 2) return 0;

            var avg = values.Average();
            var sumSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumSquares / (values.Count - 1));
        }

        /// <summary>
        /// Get all questions from a category (including subcategories).
        /// </summary>
        private static List<BenchQuestionResult> GetAllCategoryQuestions(BenchCategoryResult category)
        {
            if (category.QuestionResults != null)
                return category.QuestionResults;

            return category.SubCategoryResults?
                .SelectMany(s => s.QuestionResults)
                .ToList() ?? new List<BenchQuestionResult>();
        }

        /// <summary>
        /// Get ranking position for a score within a list.
        /// </summary>
        public static int GetRank(double score, List<double> allScores)
        {
            return allScores.Count(s => s > score) + 1;
        }

        /// <summary>
        /// Calculate improvement/degradation between two scores.
        /// </summary>
        public static double CalculateDelta(double current, double baseline)
        {
            return current - baseline;
        }

        /// <summary>
        /// Get score rating label.
        /// </summary>
        public static string GetScoreRating(double score)
        {
            return score switch
            {
                >= 95 => "Excellent",
                >= 85 => "Very Good",
                >= 75 => "Good",
                >= 60 => "Fair",
                >= 40 => "Poor",
                _ => "Very Poor"
            };
        }

        /// <summary>
        /// Get color for a score (for console/HTML output).
        /// </summary>
        public static string GetScoreColor(double score)
        {
            return score switch
            {
                >= 95 => "green",
                >= 85 => "lime",
                >= 75 => "yellow",
                >= 60 => "orange1",
                _ => "red"
            };
        }
    }

    /// <summary>
    /// Statistics for a category across all quants.
    /// </summary>
    public class CategoryStats
    {
        public string Category { get; set; } = "";
        public List<double> Scores { get; set; } = new();
        public double Average { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double StdDev { get; set; }
    }
}
