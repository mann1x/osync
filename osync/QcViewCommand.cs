// Ignore Spelling: osync

using System.Text;
using System.Text.Json;
using ByteSizeLib;
using Spectre.Console;

namespace osync
{
    /// <summary>
    /// Implementation of the qcview command
    /// Displays quantization comparison results in table or JSON format
    /// </summary>
    public class QcViewCommand
    {
        private readonly QcViewArgs _args;

        public QcViewCommand(QcViewArgs args)
        {
            _args = args;
        }

        /// <summary>
        /// Main execution method for qcview command
        /// </summary>
        public async Task<int> ExecuteAsync()
        {
            try
            {
                // Validate filename provided
                if (string.IsNullOrWhiteSpace(_args.FileName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Results file name is required[/]");
                    AnsiConsole.MarkupLine("Usage: osync qcview <filename> or osync qcview -F <filename>");
                    return 1;
                }

                // Load results file
                if (!File.Exists(_args.FileName))
                {
                    AnsiConsole.MarkupLine($"[red]Error: Results file not found: {_args.FileName}[/]");
                    return 1;
                }

                var json = await File.ReadAllTextAsync(_args.FileName);
                var resultsFile = JsonSerializer.Deserialize<QcResultsFile>(json);

                if (resultsFile == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to parse results file[/]");
                    return 1;
                }

                // Validate results file
                if (resultsFile.Results.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No results found in file[/]");
                    return 0;
                }

                var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);
                if (baseResult == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: No base quantization found in results[/]");
                    return 1;
                }

                // Calculate scores
                var scoringResults = QcScoring.CalculateScores(resultsFile);

                // Display results
                var format = string.IsNullOrWhiteSpace(_args.Format) ? "table" : _args.Format.ToLower();
                if (format == "json")
                {
                    await DisplayJsonAsync(scoringResults);
                }
                else
                {
                    await OutputTableAsync(scoringResults);
                }

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                AnsiConsole.MarkupLine($"[dim]{Markup.Escape(ex.StackTrace ?? "")}[/]");
                return 1;
            }
        }

        /// <summary>
        /// Output results as formatted table (to console or file)
        /// </summary>
        private async Task OutputTableAsync(QcScoringResults results)
        {
            if (!string.IsNullOrEmpty(_args.OutputFile))
            {
                // Output to file as plain text
                var sb = new StringBuilder();
                sb.AppendLine("Quantization Comparison Results");
                sb.AppendLine($"Model: {results.BaseModelName} | Family: {results.BaseFamily} | Size: {results.BaseParameterSize}");
                sb.AppendLine($"Test Suite: {results.TestSuiteName} ({results.TotalQuestions} questions)");

                if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
                {
                    sb.AppendLine($"Judge Model: {results.JudgeModel} (50% metrics + 50% judgment)");
                }

                sb.AppendLine();
                sb.AppendLine($"Base Quantization: {results.BaseTag}");
                sb.AppendLine($"Type: {results.BaseQuantizationType} | Size: {ByteSize.FromBytes(results.BaseDiskSizeBytes)} | Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | Prompt: {results.BasePromptTokensPerSecond:F1} tok/s");
                sb.AppendLine();
                sb.AppendLine($"Test Options: temp={results.Options.Temperature} | seed={results.Options.Seed} | top_p={results.Options.TopP} | top_k={results.Options.TopK}");
                sb.AppendLine();

                if (results.HasJudgmentScoring)
                {
                    sb.AppendLine("=== Quantization Quality & Performance (with Judgment) ===");
                }
                else
                {
                    sb.AppendLine("=== Quantization Quality & Performance ===");
                }
                sb.AppendLine();

                // Header - different columns for judgment mode
                if (results.HasJudgmentScoring)
                {
                    sb.AppendLine($"{"Tag",-25} {"Quant",-10} {"Size",-12} {"Final",-10} {"Metrics",-10} {"Judge",-10} {"Token",-10} {"Logprobs",-10} {"Eval",-12} {"Prompt",-12}");
                    sb.AppendLine(new string('-', 140));
                }
                else
                {
                    sb.AppendLine($"{"Tag",-25} {"Quant",-10} {"Size",-12} {"Overall",-10} {"Token",-10} {"Logprobs",-10} {"Length",-10} {"Perplexity",-10} {"Eval",-12} {"Prompt",-12}");
                    sb.AppendLine(new string('-', 130));
                }

                var sortedResults = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();
                foreach (var quant in sortedResults)
                {
                    var sizeStr = ByteSize.FromBytes(quant.DiskSizeBytes).ToString();
                    var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                    var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                    var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                    var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;
                    var evalSpeed = $"{quant.EvalTokensPerSecond:F1}";
                    var promptSpeed = $"{quant.PromptTokensPerSecond:F1}";

                    if (results.HasJudgmentScoring)
                    {
                        var judgeScore = quant.AverageJudgmentScore.HasValue ? $"{quant.AverageJudgmentScore.Value:F1}%" : "N/A";
                        sb.AppendLine($"{quant.Tag,-25} {quant.QuantizationType,-10} {sizeStr,-12} {quant.FinalScore:F1}%{"",-5} {quant.TotalConfidenceScore:F1}%{"",-5} {judgeScore,-10} {tokenScore:F1}%{"",-5} {logprobsScore:F1}%{"",-5} {evalSpeed,-12} {promptSpeed,-12}");
                    }
                    else
                    {
                        sb.AppendLine($"{quant.Tag,-25} {quant.QuantizationType,-10} {sizeStr,-12} {quant.TotalConfidenceScore:F1}%{"",-5} {tokenScore:F1}%{"",-5} {logprobsScore:F1}%{"",-5} {lengthScore:F1}%{"",-5} {perplexityScore:F1}%{"",-5} {evalSpeed,-12} {promptSpeed,-12}");
                    }
                }

                sb.AppendLine();
                if (results.HasJudgmentScoring)
                {
                    sb.AppendLine("=== Scores by Category (Metrics & Judgment) ===");
                }
                else
                {
                    sb.AppendLine("=== Scores by Category ===");
                }
                sb.AppendLine();

                // Category breakdown
                if (results.QuantScores.Count > 0 && results.QuantScores[0].CategoryScores.Count > 0)
                {
                    var categories = results.QuantScores[0].CategoryScores.Keys.ToList();

                    if (results.HasJudgmentScoring)
                    {
                        // Header with Metrics and Judge sub-columns for each category
                        var catHeader = $"{"Tag",-25}";
                        foreach (var cat in categories)
                        {
                            catHeader += $" {cat + " M",-10} {cat + " J",-10}";
                        }
                        sb.AppendLine(catHeader);
                        sb.AppendLine(new string('-', 25 + categories.Count * 21));

                        foreach (var quant in sortedResults)
                        {
                            var row = $"{quant.Tag,-25}";
                            foreach (var cat in categories)
                            {
                                var metricsScore = quant.CategoryScores.TryGetValue(cat, out var ms) ? ms : 0;
                                var judgeScore = quant.CategoryJudgmentScores.TryGetValue(cat, out var js) ? js : 0;
                                row += $" {metricsScore:F1}%{"",-5} {judgeScore:F1}%{"",-5}";
                            }
                            sb.AppendLine(row);
                        }
                    }
                    else
                    {
                        // Simple header without judgment
                        var catHeader = $"{"Tag",-25}";
                        foreach (var cat in categories)
                            catHeader += $" {cat,-12}";
                        sb.AppendLine(catHeader);
                        sb.AppendLine(new string('-', 25 + categories.Count * 13));

                        foreach (var quant in sortedResults)
                        {
                            var row = $"{quant.Tag,-25}";
                            foreach (var cat in categories)
                            {
                                var score = quant.CategoryScores.TryGetValue(cat, out var s) ? s : 0;
                                row += $" {score:F1}%{"",-7}";
                            }
                            sb.AppendLine(row);
                        }
                    }
                }

                await File.WriteAllTextAsync(_args.OutputFile, sb.ToString());
                var fileInfo = new FileInfo(_args.OutputFile);
                AnsiConsole.MarkupLine($"[green]Table results saved to: {_args.OutputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
            }
            else
            {
                // Output to console with colors
                DisplayTableToConsole(results);
            }
        }

        /// <summary>
        /// Display results as formatted table in console
        /// </summary>
        private void DisplayTableToConsole(QcScoringResults results)
        {
            // Header with optional judge info
            var headerText = $"[bold cyan]Quantization Comparison Results[/]\n" +
                            $"Model: [yellow]{Markup.Escape(results.BaseModelName)}[/] | " +
                            $"Family: [yellow]{Markup.Escape(results.BaseFamily)}[/] | " +
                            $"Size: [yellow]{Markup.Escape(results.BaseParameterSize)}[/]\n" +
                            $"Test Suite: [yellow]{Markup.Escape(results.TestSuiteName)}[/] ({results.TotalQuestions} questions)";

            if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
            {
                headerText += $"\nJudge Model: [magenta]{Markup.Escape(results.JudgeModel)}[/] [dim](50% metrics + 50% judgment)[/]";
            }

            var panel = new Panel(headerText)
            {
                Border = BoxBorder.Double,
                Padding = new Padding(1, 0)
            };
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine("");

            // Base model info
            var baseInfo = new Panel($"[bold]Base Quantization:[/] [cyan]{Markup.Escape(results.BaseTag)}[/]\n" +
                                    $"Type: {Markup.Escape(results.BaseQuantizationType)} | " +
                                    $"Size: {ByteSize.FromBytes(results.BaseDiskSizeBytes)} | " +
                                    $"Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | " +
                                    $"Prompt: {results.BasePromptTokensPerSecond:F1} tok/s")
            {
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(baseInfo);
            AnsiConsole.WriteLine("");

            // Test options
            DisplayTestOptions(results.Options);
            AnsiConsole.WriteLine("");

            // Main results table
            var table = new Table();
            table.Border = TableBorder.Rounded;

            // Update title based on judgment availability
            var tableTitle = results.HasJudgmentScoring
                ? "[bold yellow]Quantization Quality & Performance (with Judgment)[/]"
                : "[bold yellow]Quantization Quality & Performance[/]";
            table.Title = new TableTitle(tableTitle);

            // Add columns
            table.AddColumn(new TableColumn("[bold]Tag[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Quant[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());

            // Add judgment-specific columns when available
            if (results.HasJudgmentScoring)
            {
                table.AddColumn(new TableColumn("[bold]Final\nScore[/]").Centered());
                table.AddColumn(new TableColumn("[bold]Metrics\nScore[/]").Centered());
                table.AddColumn(new TableColumn("[bold]Judge\nScore[/]").Centered());
            }
            else
            {
                table.AddColumn(new TableColumn("[bold]Overall\nScore[/]").Centered());
            }

            table.AddColumn(new TableColumn("[bold]Token\nSimilarity[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Logprobs\nDivergence[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Length\nConsistency[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Perplexity[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Eval Speed\n(tok/s)[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Eval vs\nBase[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Prompt Speed\n(tok/s)[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Prompt vs\nBase[/]").Centered());

            // Sort by final score (or total confidence if no judgment) descending
            var sortedResults = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();

            foreach (var quant in sortedResults)
            {
                var sizeStr = ByteSize.FromBytes(quant.DiskSizeBytes).ToString();

                // Calculate component scores (average across all questions)
                var tokenScore = quant.QuestionScores?.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.TokenSimilarityScore))
                    : "N/A";
                var logprobsScore = quant.QuestionScores?.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.LogprobsDivergenceScore))
                    : "N/A";
                var lengthScore = quant.QuestionScores?.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.LengthConsistencyScore))
                    : "N/A";
                var perplexityScore = quant.QuestionScores?.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.PerplexityScore))
                    : "N/A";

                var evalSpeed = quant.EvalTokensPerSecond > 0
                    ? $"{quant.EvalTokensPerSecond:F1}"
                    : "N/A";

                var evalVsBase = quant.EvalPerformancePercent > 0
                    ? FormatPerformance(quant.EvalPerformancePercent)
                    : "N/A";

                var promptSpeed = quant.PromptTokensPerSecond > 0
                    ? $"{quant.PromptTokensPerSecond:F1}"
                    : "N/A";

                var promptVsBase = quant.PromptPerformancePercent > 0
                    ? FormatPerformance(quant.PromptPerformancePercent)
                    : "N/A";

                if (results.HasJudgmentScoring)
                {
                    var finalScore = FormatScore(quant.FinalScore);
                    var metricsScore = FormatScore(quant.TotalConfidenceScore);
                    var judgeScore = quant.AverageJudgmentScore.HasValue
                        ? FormatScore(quant.AverageJudgmentScore.Value)
                        : "N/A";

                    table.AddRow(
                        Markup.Escape(quant.Tag),
                        Markup.Escape(quant.QuantizationType),
                        sizeStr,
                        finalScore,
                        metricsScore,
                        judgeScore,
                        tokenScore,
                        logprobsScore,
                        lengthScore,
                        perplexityScore,
                        evalSpeed,
                        evalVsBase,
                        promptSpeed,
                        promptVsBase
                    );
                }
                else
                {
                    var overallScore = FormatScore(quant.TotalConfidenceScore);

                    table.AddRow(
                        Markup.Escape(quant.Tag),
                        Markup.Escape(quant.QuantizationType),
                        sizeStr,
                        overallScore,
                        tokenScore,
                        logprobsScore,
                        lengthScore,
                        perplexityScore,
                        evalSpeed,
                        evalVsBase,
                        promptSpeed,
                        promptVsBase
                    );
                }
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine("");

            // Category breakdown table
            DisplayCategoryBreakdown(results);
        }

        /// <summary>
        /// Display category-by-category breakdown
        /// </summary>
        private void DisplayCategoryBreakdown(QcScoringResults results)
        {
            // Get all categories from first quant
            if (results.QuantScores.Count == 0)
                return;

            var categories = results.QuantScores[0].CategoryScores.Keys.ToList();
            if (categories.Count == 0)
                return;

            var table = new Table();
            table.Border = TableBorder.Rounded;

            // Update title based on judgment availability
            var tableTitle = results.HasJudgmentScoring
                ? "[bold yellow]Scores by Category (Metrics & Judgment)[/]"
                : "[bold yellow]Scores by Category[/]";
            table.Title = new TableTitle(tableTitle);

            // Add Tag column
            table.AddColumn(new TableColumn("[bold]Tag[/]").LeftAligned());

            // Add category columns - with sub-columns for Metrics and Judge when judgment is available
            foreach (var category in categories)
            {
                if (results.HasJudgmentScoring)
                {
                    // Add two columns per category: Metrics and Judge
                    table.AddColumn(new TableColumn($"[bold]{category}[/]\n[dim]Metrics[/]").Centered());
                    table.AddColumn(new TableColumn($"[bold]{category}[/]\n[dim]Judge[/]").Centered());
                }
                else
                {
                    table.AddColumn(new TableColumn($"[bold]{category}[/]").Centered());
                }
            }

            // Sort by final score (which considers both metrics and judgment when available)
            var sortedResults = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();

            foreach (var quant in sortedResults)
            {
                var row = new List<string> { quant.Tag };

                foreach (var category in categories)
                {
                    // Add metrics score
                    if (quant.CategoryScores.TryGetValue(category, out var metricsScore))
                    {
                        row.Add(FormatScore(metricsScore));
                    }
                    else
                    {
                        row.Add("N/A");
                    }

                    // Add judgment score if judgment scoring is available
                    if (results.HasJudgmentScoring)
                    {
                        if (quant.CategoryJudgmentScores.TryGetValue(category, out var judgmentScore))
                        {
                            row.Add(FormatScore(judgmentScore));
                        }
                        else
                        {
                            row.Add("N/A");
                        }
                    }
                }

                table.AddRow(row.ToArray());
            }

            AnsiConsole.Write(table);
        }

        /// <summary>
        /// Display test options used
        /// </summary>
        private void DisplayTestOptions(QcTestOptions options)
        {
            if (options == null)
                return;

            var optionsText = $"[dim]Test Options:[/] " +
                            $"temp={options.Temperature} | " +
                            $"seed={options.Seed} | " +
                            $"top_p={options.TopP} | " +
                            $"top_k={options.TopK}";

            if (options.RepeatPenalty.HasValue)
                optionsText += $" | repeat_penalty={options.RepeatPenalty.Value}";

            if (options.FrequencyPenalty.HasValue)
                optionsText += $" | frequency_penalty={options.FrequencyPenalty.Value}";

            AnsiConsole.MarkupLine(optionsText);
        }

        /// <summary>
        /// Format score as colored percentage
        /// </summary>
        private string FormatScore(double score)
        {
            var color = GetScoreColor(score);
            return $"[{color}]{score:F1}%[/]";
        }

        /// <summary>
        /// Get color based on score value
        /// Lime (80-100%): Excellent/Very good quality preservation
        /// Yellow (70-80%): Good quality
        /// Orange (50-70%): Moderate quality loss
        /// Red (below 50%): Significant quality degradation
        /// </summary>
        private string GetScoreColor(double score)
        {
            if (score >= 80) return "lime";
            if (score >= 70) return "yellow";
            if (score >= 50) return "orange1";
            return "red";
        }

        /// <summary>
        /// Format performance percentage
        /// </summary>
        private string FormatPerformance(double percent)
        {
            var color = "white";
            if (percent > 100) color = "lime";
            else if (percent < 100) color = "orange1";

            var arrow = percent > 100 ? "↑" : (percent < 100 ? "↓" : "=");
            return $"[{color}]{percent:F0}% {arrow}[/]";
        }

        /// <summary>
        /// Output results as JSON (console or file)
        /// </summary>
        private async Task DisplayJsonAsync(QcScoringResults results)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(results, options);

            if (string.IsNullOrEmpty(_args.OutputFile))
            {
                // Output to console - use Console.WriteLine to avoid markup parsing issues
                AnsiConsole.MarkupLine("[yellow]JSON Results:[/]");
                Console.WriteLine(json);
            }
            else
            {
                // Output to file
                await File.WriteAllTextAsync(_args.OutputFile, json);
                var fileInfo = new FileInfo(_args.OutputFile);
                AnsiConsole.MarkupLine($"[green]JSON results saved to: {_args.OutputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
            }
        }
    }
}
