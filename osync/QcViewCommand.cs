// Ignore Spelling: osync

using System.Text;
using System.Text.Json;
using ByteSizeLib;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
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

                // Apply --repo override if specified
                if (!string.IsNullOrWhiteSpace(_args.Repository))
                {
                    resultsFile.RepositoryUrl = _args.Repository;
                }

                // Apply --metricsonly: strip all judgment data
                if (_args.MetricsOnly)
                {
                    foreach (var quantResult in resultsFile.Results)
                    {
                        foreach (var questionResult in quantResult.QuestionResults)
                        {
                            questionResult.Judgment = null;
                        }
                    }
                    AnsiConsole.MarkupLine("[dim]Note: --metricsonly specified, judgment data ignored[/]");
                }

                var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);
                if (baseResult == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: No base quantization found in results[/]");
                    return 1;
                }

                // Display results
                var format = string.IsNullOrWhiteSpace(_args.Format) ? "table" : _args.Format.ToLower();
                var isFileOutput = !string.IsNullOrEmpty(_args.OutputFile) && format != "table";

                if (isFileOutput)
                {
                    // File output with progress bar
                    await AnsiConsole.Progress()
                        .AutoClear(false)
                        .HideCompleted(false)
                        .Columns(
                            new TaskDescriptionColumn(),
                            new ProgressBarColumn(),
                            new PercentageColumn(),
                            new SpinnerColumn())
                        .StartAsync(async ctx =>
                        {
                            var mainTask = ctx.AddTask("[cyan]Generating report[/]", maxValue: 100);

                            // Calculate scores
                            mainTask.Description = "[cyan]Calculating scores...[/]";
                            var scoringResults = QcScoring.CalculateScores(resultsFile);
                            mainTask.Increment(20);

                            // Generate output based on format
                            switch (format)
                            {
                                case "json":
                                    mainTask.Description = "[cyan]Writing JSON...[/]";
                                    await DisplayJsonAsync(scoringResults);
                                    break;
                                case "md":
                                case "markdown":
                                    mainTask.Description = "[cyan]Generating Markdown...[/]";
                                    await OutputMarkdownAsync(scoringResults, resultsFile);
                                    break;
                                case "html":
                                    mainTask.Description = "[cyan]Generating HTML...[/]";
                                    await OutputHtmlAsync(scoringResults, resultsFile);
                                    break;
                                case "pdf":
                                    mainTask.Description = "[cyan]Generating PDF (this may take a while)...[/]";
                                    await OutputPdfWithProgressAsync(scoringResults, resultsFile, mainTask);
                                    break;
                            }
                            mainTask.Value = 100;
                            mainTask.Description = "[green]Complete[/]";
                        });
                }
                else
                {
                    // Console output (no progress bar needed)
                    var scoringResults = QcScoring.CalculateScores(resultsFile);
                    switch (format)
                    {
                        case "json":
                            await DisplayJsonAsync(scoringResults);
                            break;
                        case "md":
                        case "markdown":
                            await OutputMarkdownAsync(scoringResults, resultsFile);
                            break;
                        case "html":
                            await OutputHtmlAsync(scoringResults, resultsFile);
                            break;
                        case "pdf":
                            await OutputPdfAsync(scoringResults, resultsFile);
                            break;
                        default:
                            await OutputTableAsync(scoringResults);
                            break;
                    }
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

                if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
                {
                    sb.AppendLine($"Repository: {results.RepositoryUrl}");
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
                    sb.AppendLine($"{"Tag",-25} {"Quant",-20} {"Size",-12} {"Final",-10} {"Metrics",-10} {"Judge",-10} {"Best",-20} {"Token",-10} {"Logprobs",-10} {"Eval",-12} {"Prompt",-12}");
                    sb.AppendLine(new string('-', 170));
                }
                else
                {
                    sb.AppendLine($"{"Tag",-25} {"Quant",-20} {"Size",-12} {"Overall",-10} {"Token",-10} {"Logprobs",-10} {"Length",-10} {"Perplexity",-10} {"Eval",-12} {"Prompt",-12}");
                    sb.AppendLine(new string('-', 140));
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

                    var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                    if (results.HasJudgmentScoring)
                    {
                        var judgeScore = quant.AverageJudgmentScore.HasValue ? $"{quant.AverageJudgmentScore.Value:F1}%" : "N/A";
                        var bestStats = FormatBestStatsPlain(quant);
                        sb.AppendLine($"{quant.Tag,-25} {quantDisplay,-20} {sizeStr,-12} {quant.FinalScore:F1}%{"",-5} {quant.TotalConfidenceScore:F1}%{"",-5} {judgeScore,-10} {bestStats,-20} {tokenScore:F1}%{"",-5} {logprobsScore:F1}%{"",-5} {evalSpeed,-12} {promptSpeed,-12}");
                    }
                    else
                    {
                        sb.AppendLine($"{quant.Tag,-25} {quantDisplay,-20} {sizeStr,-12} {quant.TotalConfidenceScore:F1}%{"",-5} {tokenScore:F1}%{"",-5} {logprobsScore:F1}%{"",-5} {lengthScore:F1}%{"",-5} {perplexityScore:F1}%{"",-5} {evalSpeed,-12} {promptSpeed,-12}");
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

            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                headerText += $"\nRepository: [blue]{Markup.Escape(results.RepositoryUrl)}[/]";
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
            table.AddColumn(new TableColumn("[bold]Tag[/]").LeftAligned());
            table.AddColumn(new TableColumn("[bold]Quant[/]").LeftAligned());
            table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());

            // Add judgment-specific columns when available
            if (results.HasJudgmentScoring)
            {
                table.AddColumn(new TableColumn("[bold]Final\nScore[/]").Centered());
                table.AddColumn(new TableColumn("[bold]Metrics\nScore[/]").Centered());
                table.AddColumn(new TableColumn("[bold]Judge\nScore[/]").Centered());
                table.AddColumn(new TableColumn("[bold]Judge\nBest Answers[/]").Centered());
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

                    // Format best answer stats: "B:5 A:3 AB:2" style with percentage
                    var judgeBest = FormatBestStats(quant);

                    var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                    table.AddRow(
                        Markup.Escape(quant.Tag),
                        Markup.Escape(quantDisplay),
                        sizeStr,
                        finalScore,
                        metricsScore,
                        judgeScore,
                        judgeBest,
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
                    var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;

                    table.AddRow(
                        Markup.Escape(quant.Tag),
                        Markup.Escape(quantDisplay),
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
        /// Format best answer statistics showing percentage and counts.
        /// Shows quant win percentage (B wins) including ties in denominator.
        /// Ties count as wins for base (A), so formula is B / (A + B + AB).
        /// Format: "67% (B:10 A:5 =:3)" meaning quant won 67% of all comparisons
        /// </summary>
        private string FormatBestStats(QuantScoreResult quant)
        {
            var total = quant.BestCount + quant.WorstCount + quant.TieCount;
            if (total == 0)
                return "[dim]N/A[/]";

            // Calculate percentage - B wins vs total (ties count as A wins)
            var bestPercent = (quant.BestCount * 100.0) / total;

            // Color based on how well quant performed vs base
            var color = bestPercent >= 60 ? "lime" : bestPercent >= 40 ? "yellow" : "red";

            // Format: "67% (B:10 A:5 =:3)"
            return $"[{color}]{bestPercent:F0}%[/] [dim](B:{quant.BestCount} A:{quant.WorstCount} =:{quant.TieCount})[/]";
        }

        /// <summary>
        /// Format best answer statistics for plain text (no colors).
        /// Ties count as wins for base (A), so formula is B / (A + B + AB).
        /// </summary>
        private string FormatBestStatsPlain(QuantScoreResult quant)
        {
            var total = quant.BestCount + quant.WorstCount + quant.TieCount;
            if (total == 0)
                return "N/A";

            // Calculate percentage - B wins vs total (ties count as A wins)
            var bestPercent = (quant.BestCount * 100.0) / total;
            return $"{bestPercent:F0}% (B:{quant.BestCount} A:{quant.WorstCount} =:{quant.TieCount})";
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

        /// <summary>
        /// Output results as Markdown file
        /// </summary>
        private async Task OutputMarkdownAsync(QcScoringResults results, QcResultsFile resultsFile)
        {
            var sb = new StringBuilder();

            // Header
            sb.AppendLine("# Quantization Comparison Results");
            sb.AppendLine();
            sb.AppendLine($"**Model:** {results.BaseModelName}  ");
            sb.AppendLine($"**Family:** {results.BaseFamily}  ");
            sb.AppendLine($"**Parameter Size:** {results.BaseParameterSize}  ");
            sb.AppendLine($"**Test Suite:** {results.TestSuiteName} ({results.TotalQuestions} questions)  ");

            if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
            {
                sb.AppendLine($"**Judge Model:** {results.JudgeModel} (50% metrics + 50% judgment)  ");
            }

            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                sb.AppendLine($"**Repository:** [{results.RepositoryUrl}]({results.RepositoryUrl})  ");
            }

            sb.AppendLine();

            // Base Quantization Info
            sb.AppendLine("## Base Quantization");
            sb.AppendLine();
            sb.AppendLine($"- **Tag:** {results.BaseTag}");
            sb.AppendLine($"- **Type:** {results.BaseQuantizationType}");
            sb.AppendLine($"- **Size:** {ByteSize.FromBytes(results.BaseDiskSizeBytes)}");
            sb.AppendLine($"- **Eval Speed:** {results.BaseEvalTokensPerSecond:F1} tok/s");
            sb.AppendLine($"- **Prompt Speed:** {results.BasePromptTokensPerSecond:F1} tok/s");
            sb.AppendLine();

            // Test Options
            sb.AppendLine("## Test Options");
            sb.AppendLine();
            sb.AppendLine($"| Parameter | Value |");
            sb.AppendLine($"|-----------|-------|");
            sb.AppendLine($"| Temperature | {results.Options.Temperature} |");
            sb.AppendLine($"| Seed | {results.Options.Seed} |");
            sb.AppendLine($"| Top P | {results.Options.TopP} |");
            sb.AppendLine($"| Top K | {results.Options.TopK} |");
            if (results.Options.RepeatPenalty.HasValue)
                sb.AppendLine($"| Repeat Penalty | {results.Options.RepeatPenalty} |");
            if (results.Options.FrequencyPenalty.HasValue)
                sb.AppendLine($"| Frequency Penalty | {results.Options.FrequencyPenalty} |");
            sb.AppendLine();

            // Main Results Table
            sb.AppendLine("## Quantization Comparison");
            sb.AppendLine();

            if (results.HasJudgmentScoring)
            {
                sb.AppendLine("| Tag | Quant | Size | Final | Metrics | Judge | Best | Token Sim | Logprobs | Length | Perplexity | Eval | Prompt |");
                sb.AppendLine("|-----|-------|------|-------|---------|-------|------|-----------|----------|--------|------------|------|--------|");

                foreach (var quant in results.QuantScores.OrderByDescending(q => q.FinalScore))
                {
                    var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                    var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                    var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                    var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                    var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;
                    var bestStats = FormatBestStatsPlain(quant);

                    sb.AppendLine($"| {quant.Tag} | {quantDisplay} | {ByteSize.FromBytes(quant.DiskSizeBytes)} | {quant.FinalScore:F1}% | {quant.TotalConfidenceScore:F1}% | {quant.AverageJudgmentScore?.ToString("F1") ?? "N/A"}% | {bestStats} | {tokenScore:F1}% | {logprobsScore:F1}% | {lengthScore:F1}% | {perplexityScore:F1}% | {quant.EvalTokensPerSecond:F1} | {quant.PromptTokensPerSecond:F1} |");
                }
            }
            else
            {
                sb.AppendLine("| Tag | Quant | Size | Score | Token Sim | Logprobs | Length | Perplexity | Eval | Prompt |");
                sb.AppendLine("|-----|-------|------|-------|-----------|----------|--------|------------|------|--------|");

                foreach (var quant in results.QuantScores.OrderByDescending(q => q.TotalConfidenceScore))
                {
                    var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                    var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                    var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                    var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                    var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;

                    sb.AppendLine($"| {quant.Tag} | {quantDisplay} | {ByteSize.FromBytes(quant.DiskSizeBytes)} | {quant.TotalConfidenceScore:F1}% | {tokenScore:F1}% | {logprobsScore:F1}% | {lengthScore:F1}% | {perplexityScore:F1}% | {quant.EvalTokensPerSecond:F1} | {quant.PromptTokensPerSecond:F1} |");
                }
            }

            sb.AppendLine();

            // Category Breakdown
            if (results.QuantScores.Any() && results.QuantScores.First().CategoryScores.Any())
            {
                sb.AppendLine("## Scores by Category");
                sb.AppendLine();

                var categories = results.QuantScores.First().CategoryScores.Keys.ToList();

                // Build header
                var header = "| Tag |";
                var separator = "|-----|";
                foreach (var cat in categories)
                {
                    header += $" {cat} Metrics |";
                    separator += "--------|";
                    if (results.HasJudgmentScoring)
                    {
                        header += $" {cat} Judge |";
                        separator += "--------|";
                    }
                }
                sb.AppendLine(header);
                sb.AppendLine(separator);

                foreach (var quant in results.QuantScores.OrderByDescending(q => q.FinalScore))
                {
                    var row = $"| {quant.Tag} |";
                    foreach (var cat in categories)
                    {
                        row += $" {quant.CategoryScores.GetValueOrDefault(cat):F1}% |";
                        if (results.HasJudgmentScoring)
                        {
                            row += $" {quant.CategoryJudgmentScores.GetValueOrDefault(cat):F1}% |";
                        }
                    }
                    sb.AppendLine(row);
                }

                sb.AppendLine();
            }

            // Rankings Section
            sb.AppendLine("## Rankings");
            sb.AppendLine();

            // Ranked by Final/Metrics Score
            sb.AppendLine("### Ranked by " + (results.HasJudgmentScoring ? "Final Score" : "Metrics Score"));
            sb.AppendLine();
            sb.AppendLine("| Rank | Tag | Quant | Score |");
            sb.AppendLine("|------|-----|-------|-------|");
            var rankByScore = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();
            for (int i = 0; i < rankByScore.Count; i++)
            {
                var q = rankByScore[i];
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {q.FinalScore:F1}% |");
            }
            sb.AppendLine();

            // Ranked by Metrics Score (only if has judgment, otherwise it's the same)
            if (results.HasJudgmentScoring)
            {
                sb.AppendLine("### Ranked by Metrics Score");
                sb.AppendLine();
                sb.AppendLine("| Rank | Tag | Quant | Score |");
                sb.AppendLine("|------|-----|-------|-------|");
                var rankByMetrics = results.QuantScores.OrderByDescending(q => q.TotalConfidenceScore).ToList();
                for (int i = 0; i < rankByMetrics.Count; i++)
                {
                    var q = rankByMetrics[i];
                    var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                    sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {q.TotalConfidenceScore:F1}% |");
                }
                sb.AppendLine();

                // Ranked by Judge Score
                sb.AppendLine("### Ranked by Judge Score");
                sb.AppendLine();
                sb.AppendLine("| Rank | Tag | Quant | Score |");
                sb.AppendLine("|------|-----|-------|-------|");
                var rankByJudge = results.QuantScores.Where(q => q.AverageJudgmentScore.HasValue)
                    .OrderByDescending(q => q.AverageJudgmentScore!.Value).ToList();
                for (int i = 0; i < rankByJudge.Count; i++)
                {
                    var q = rankByJudge[i];
                    var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                    sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {q.AverageJudgmentScore:F1}% |");
                }
                sb.AppendLine();

                // Ranked by Judge Best Answers
                sb.AppendLine("### Ranked by Judge Best Answers");
                sb.AppendLine();
                sb.AppendLine("| Rank | Tag | Quant | Win % | B Wins | A Wins | Ties |");
                sb.AppendLine("|------|-----|-------|-------|--------|--------|------|");
                var rankByBest = results.QuantScores.OrderByDescending(q =>
                {
                    var total = q.BestCount + q.WorstCount + q.TieCount;
                    return total > 0 ? (q.BestCount * 100.0 / total) : 0;
                }).ToList();
                for (int i = 0; i < rankByBest.Count; i++)
                {
                    var q = rankByBest[i];
                    var total = q.BestCount + q.WorstCount + q.TieCount;
                    var winPct = total > 0 ? (q.BestCount * 100.0 / total) : 0;
                    var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                    sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {winPct:F0}% | {q.BestCount} | {q.WorstCount} | {q.TieCount} |");
                }
                sb.AppendLine();
            }

            // Ranked by Eval Speed
            sb.AppendLine("### Ranked by Eval Speed");
            sb.AppendLine();
            sb.AppendLine("| Rank | Tag | Quant | Speed | vs Base |");
            sb.AppendLine("|------|-----|-------|-------|---------|");
            var rankByEval = results.QuantScores.OrderByDescending(q => q.EvalTokensPerSecond).ToList();
            for (int i = 0; i < rankByEval.Count; i++)
            {
                var q = rankByEval[i];
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {q.EvalTokensPerSecond:F1} tok/s | {q.EvalPerformancePercent:F0}% |");
            }
            sb.AppendLine();

            // Ranked by Perplexity Score
            sb.AppendLine("### Ranked by Perplexity Score");
            sb.AppendLine();
            sb.AppendLine("| Rank | Tag | Quant | Score |");
            sb.AppendLine("|------|-----|-------|-------|");
            var rankByPerplexity = results.QuantScores.OrderByDescending(q =>
                q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0).ToList();
            for (int i = 0; i < rankByPerplexity.Count; i++)
            {
                var q = rankByPerplexity[i];
                var perplexityScore = q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0;
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"| {i + 1} | {q.Tag} | {quantDisplay} | {perplexityScore:F1}% |");
            }
            sb.AppendLine();

            // Footer
            sb.AppendLine("---");
            sb.AppendLine($"*Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss}*");

            // Output
            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".md");
            }

            await File.WriteAllTextAsync(outputFile, sb.ToString());
            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]Markdown results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Output results as HTML file with interactive features
        /// </summary>
        private async Task OutputHtmlAsync(QcScoringResults results, QcResultsFile resultsFile)
        {
            var sb = new StringBuilder();

            // HTML Header with embedded CSS and JavaScript
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset=\"UTF-8\">");
            sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
            sb.AppendLine($"    <title>Quantization Comparison - {EscapeHtml(results.BaseModelName)}</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine(@"
        :root {
            --bg-primary: #0d1117;
            --bg-secondary: #161b22;
            --bg-tertiary: #21262d;
            --text-primary: #f0f6fc;
            --text-secondary: #8b949e;
            --accent-primary: #58a6ff;
            --accent-green: #3fb950;
            --accent-yellow: #d29922;
            --accent-red: #f85149;
            --accent-purple: #a371f7;
            --border-color: #30363d;
        }
        * { box-sizing: border-box; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif;
            background: var(--bg-primary);
            color: var(--text-primary);
            margin: 0;
            padding: 30px;
            line-height: 1.6;
            font-size: 14px;
        }
        .container { max-width: 1800px; margin: 0 auto; }
        h1 { font-size: 2.5em; color: var(--text-primary); margin: 0 0 20px 0; font-weight: 600; }
        h2 { font-size: 1.5em; color: var(--accent-primary); margin: 30px 0 15px 0; font-weight: 600; border-bottom: 1px solid var(--border-color); padding-bottom: 10px; }
        h3 { font-size: 1.2em; color: var(--text-primary); margin: 20px 0 10px 0; }
        h4 { font-size: 1em; color: var(--accent-primary); margin: 15px 0 8px 0; }
        .header-card {
            background: linear-gradient(135deg, var(--bg-secondary) 0%, var(--bg-tertiary) 100%);
            padding: 30px;
            border-radius: 12px;
            margin-bottom: 25px;
            border: 1px solid var(--border-color);
            box-shadow: 0 4px 6px rgba(0,0,0,0.3);
        }
        .info-grid {
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
            gap: 20px;
            margin-top: 15px;
        }
        .info-item {
            background: var(--bg-primary);
            padding: 15px;
            border-radius: 8px;
            border: 1px solid var(--border-color);
        }
        .info-label { color: var(--text-secondary); font-size: 0.85em; text-transform: uppercase; letter-spacing: 0.5px; display: block; margin-bottom: 5px; }
        .info-value { font-weight: 600; font-size: 1.1em; color: var(--text-primary); word-break: break-word; }
        table {
            width: 100%;
            border-collapse: separate;
            border-spacing: 0;
            margin: 20px 0;
            background: var(--bg-secondary);
            border-radius: 12px;
            overflow: hidden;
            border: 1px solid var(--border-color);
            font-size: 13px;
        }
        th, td {
            padding: 14px 12px;
            text-align: center;
            border-bottom: 1px solid var(--border-color);
        }
        th {
            background: var(--bg-tertiary);
            color: var(--text-primary);
            font-weight: 600;
            text-transform: uppercase;
            font-size: 0.75em;
            letter-spacing: 0.5px;
        }
        tbody tr:hover { background: rgba(88, 166, 255, 0.1); }
        tbody tr:last-child td { border-bottom: none; }
        .score-high { color: var(--accent-green); font-weight: 700; }
        .score-mid { color: var(--accent-yellow); font-weight: 600; }
        .score-low { color: var(--accent-red); font-weight: 600; }
        .speed-high { color: var(--accent-green); font-weight: 700; }
        .speed-mid { color: var(--accent-yellow); font-weight: 600; }
        .speed-low { color: var(--accent-red); font-weight: 600; }
        .collapsible {
            background: var(--bg-secondary);
            color: var(--text-primary);
            cursor: pointer;
            padding: 18px 20px;
            width: 100%;
            border: 1px solid var(--border-color);
            text-align: left;
            outline: none;
            font-size: 1.1em;
            font-weight: 600;
            border-radius: 8px;
            margin: 8px 0;
            display: flex;
            justify-content: space-between;
            align-items: center;
            transition: all 0.2s ease;
        }
        .collapsible:hover { background: var(--bg-tertiary); border-color: var(--accent-primary); }
        .collapsible:after {
            content: '+';
            color: var(--accent-primary);
            font-weight: bold;
            font-size: 1.4em;
        }
        .collapsible.active { background: var(--bg-tertiary); border-color: var(--accent-primary); }
        .collapsible.active:after { content: '−'; }
        .content {
            max-height: 0;
            overflow: hidden;
            transition: max-height 0.3s ease-out;
            background: var(--bg-secondary);
            border-radius: 0 0 8px 8px;
            margin-top: -8px;
            border: 1px solid var(--border-color);
            border-top: none;
        }
        .content-inner { padding: 20px; }
        .qa-card {
            background: var(--bg-tertiary);
            padding: 20px;
            margin: 15px 0;
            border-radius: 10px;
            border-left: 4px solid var(--accent-primary);
        }
        .qa-question { font-weight: 600; margin-bottom: 15px; color: var(--text-primary); font-size: 1.05em; }
        .qa-answer {
            white-space: pre-wrap;
            font-family: 'SF Mono', 'Consolas', 'Liberation Mono', Menlo, monospace;
            font-size: 0.9em;
            background: var(--bg-primary);
            padding: 15px;
            border-radius: 8px;
            border: 1px solid var(--border-color);
            color: var(--text-secondary);
            line-height: 1.5;
            overflow-x: auto;
        }
        .judgment-badge {
            display: inline-block;
            padding: 5px 12px;
            border-radius: 20px;
            font-size: 0.85em;
            font-weight: 600;
            margin: 5px 8px 5px 0;
        }
        .best-b { background: var(--accent-green); color: #000; }
        .best-a { background: var(--accent-red); color: #fff; }
        .best-tie { background: var(--accent-yellow); color: #000; }
        .badge-score { background: var(--accent-purple); color: #fff; }
        a { color: var(--accent-primary); text-decoration: none; }
        a:hover { text-decoration: underline; }
        .theme-toggle {
            position: fixed;
            top: 20px;
            right: 20px;
            padding: 12px 18px;
            border: 1px solid var(--border-color);
            border-radius: 8px;
            cursor: pointer;
            background: var(--bg-secondary);
            color: var(--text-primary);
            font-weight: 500;
            transition: all 0.2s;
            z-index: 1000;
        }
        .theme-toggle:hover { background: var(--bg-tertiary); border-color: var(--accent-primary); }
        body.light-theme {
            --bg-primary: #ffffff;
            --bg-secondary: #f6f8fa;
            --bg-tertiary: #eaeef2;
            --text-primary: #1f2328;
            --text-secondary: #656d76;
            --border-color: #d0d7de;
        }
        .reason-text { color: var(--text-secondary); font-style: italic; margin: 10px 0; padding: 10px; background: var(--bg-primary); border-radius: 6px; border-left: 3px solid var(--accent-purple); }
        .category-table th { font-size: 0.7em; padding: 10px 8px; }
        .category-table td { padding: 10px 8px; font-size: 0.9em; }
    ");
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
            sb.AppendLine("<button class=\"theme-toggle\" onclick=\"toggleTheme()\">🌓 Toggle Theme</button>");
            sb.AppendLine("<div class=\"container\">");

            // Header
            sb.AppendLine("<div class=\"header-card\">");
            sb.AppendLine($"<h1>📊 Quantization Comparison Results</h1>");
            sb.AppendLine("<div class=\"info-grid\">");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Model</span><span class=\"info-value\">{EscapeHtml(results.BaseModelName)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Family</span><span class=\"info-value\">{EscapeHtml(results.BaseFamily)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Parameter Size</span><span class=\"info-value\">{EscapeHtml(results.BaseParameterSize)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Test Suite</span><span class=\"info-value\">{EscapeHtml(results.TestSuiteName)} ({results.TotalQuestions} questions)</span></div>");

            if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
            {
                sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Judge Model</span><span class=\"info-value\">{EscapeHtml(results.JudgeModel)}</span></div>");
            }

            sb.AppendLine("</div>"); // Close info-grid

            // Repository as full-width row below the grid
            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                sb.AppendLine($"<div class=\"info-item\" style=\"grid-column: 1 / -1; margin-top: 0.5rem;\"><span class=\"info-label\">Repository</span><span class=\"info-value\"><a href=\"{EscapeHtml(results.RepositoryUrl)}\" target=\"_blank\">{EscapeHtml(results.RepositoryUrl)}</a></span></div>");
            }

            sb.AppendLine("</div>"); // Close header-card

            // Base Info
            sb.AppendLine("<div class=\"header-card\">");
            sb.AppendLine($"<h3>🎯 Base Quantization: {EscapeHtml(results.BaseTag)}</h3>");
            sb.AppendLine("<div class=\"info-grid\">");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Type</span><span class=\"info-value\">{EscapeHtml(results.BaseQuantizationType)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Disk Size</span><span class=\"info-value\">{ByteSize.FromBytes(results.BaseDiskSizeBytes)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Eval Speed</span><span class=\"info-value\">{results.BaseEvalTokensPerSecond:F1} tok/s</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Prompt Speed</span><span class=\"info-value\">{results.BasePromptTokensPerSecond:F1} tok/s</span></div>");
            sb.AppendLine("</div></div>");

            // Main Results Table
            sb.AppendLine("<h2>📈 Quantization Quality & Performance</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr>");
            sb.AppendLine("<th>Tag</th><th>Quantization</th><th>Size</th>");
            if (results.HasJudgmentScoring)
            {
                sb.AppendLine("<th>Final Score</th><th>Metrics</th><th>Judge</th><th>Best Answers</th>");
            }
            else
            {
                sb.AppendLine("<th>Score</th>");
            }
            sb.AppendLine("<th>Token Sim</th><th>Logprobs</th><th>Length</th><th>Perplexity</th><th>Eval Speed</th><th>Prompt Speed</th>");
            sb.AppendLine("</tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var quant in results.QuantScores.OrderByDescending(q => q.FinalScore))
            {
                var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td><strong>{EscapeHtml(quant.Tag)}</strong></td>");
                sb.AppendLine($"<td>{EscapeHtml(quantDisplay)}</td>");
                sb.AppendLine($"<td>{ByteSize.FromBytes(quant.DiskSizeBytes)}</td>");

                if (results.HasJudgmentScoring)
                {
                    sb.AppendLine($"<td class=\"{GetScoreClass(quant.FinalScore)}\">{quant.FinalScore:F1}%</td>");
                    sb.AppendLine($"<td class=\"{GetScoreClass(quant.TotalConfidenceScore)}\">{quant.TotalConfidenceScore:F1}%</td>");
                    sb.AppendLine($"<td class=\"{GetScoreClass(quant.AverageJudgmentScore ?? 0)}\">{quant.AverageJudgmentScore?.ToString("F1") ?? "N/A"}%</td>");
                    sb.AppendLine($"<td>{FormatBestStatsHtml(quant)}</td>");
                }
                else
                {
                    sb.AppendLine($"<td class=\"{GetScoreClass(quant.TotalConfidenceScore)}\">{quant.TotalConfidenceScore:F1}%</td>");
                }

                sb.AppendLine($"<td class=\"{GetScoreClass(tokenScore)}\">{tokenScore:F1}%</td>");
                sb.AppendLine($"<td class=\"{GetScoreClass(logprobsScore)}\">{logprobsScore:F1}%</td>");
                sb.AppendLine($"<td class=\"{GetScoreClass(lengthScore)}\">{lengthScore:F1}%</td>");
                sb.AppendLine($"<td class=\"{GetScoreClass(perplexityScore)}\">{perplexityScore:F1}%</td>");
                sb.AppendLine($"<td class=\"{GetSpeedClass(quant.EvalPerformancePercent)}\">{quant.EvalTokensPerSecond:F1} tok/s ({quant.EvalPerformancePercent:F0}%)</td>");
                sb.AppendLine($"<td class=\"{GetSpeedClass(quant.PromptPerformancePercent)}\">{quant.PromptTokensPerSecond:F1} tok/s ({quant.PromptPerformancePercent:F0}%)</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</tbody></table>");

            // Category Scores Table
            if (results.QuantScores.Any() && results.QuantScores.First().CategoryScores.Any())
            {
                sb.AppendLine("<h2>📋 Scores by Category</h2>");
                sb.AppendLine("<table class=\"category-table\">");
                sb.AppendLine("<thead><tr>");
                sb.AppendLine("<th>Tag</th>");

                var categories = results.QuantScores.First().CategoryScores.Keys.ToList();
                foreach (var cat in categories)
                {
                    sb.AppendLine($"<th>{EscapeHtml(cat)}<br>Metrics</th>");
                    if (results.HasJudgmentScoring)
                    {
                        sb.AppendLine($"<th>{EscapeHtml(cat)}<br>Judge</th>");
                    }
                }
                sb.AppendLine("</tr></thead>");
                sb.AppendLine("<tbody>");

                foreach (var quant in results.QuantScores.OrderByDescending(q => q.FinalScore))
                {
                    sb.AppendLine("<tr>");
                    sb.AppendLine($"<td><strong>{EscapeHtml(quant.Tag)}</strong></td>");

                    foreach (var cat in categories)
                    {
                        var metricScore = quant.CategoryScores.GetValueOrDefault(cat);
                        sb.AppendLine($"<td class=\"{GetScoreClass(metricScore)}\">{metricScore:F1}%</td>");

                        if (results.HasJudgmentScoring)
                        {
                            var judgeScore = quant.CategoryJudgmentScores.GetValueOrDefault(cat);
                            sb.AppendLine($"<td class=\"{GetScoreClass(judgeScore)}\">{judgeScore:F1}%</td>");
                        }
                    }
                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</tbody></table>");
            }

            // Rankings Section
            sb.AppendLine("<h2>🏆 Rankings</h2>");

            // Create ranking tables in a grid layout
            sb.AppendLine("<div style=\"display:grid;grid-template-columns:repeat(auto-fit, minmax(350px, 1fr));gap:20px;\">");

            // Ranked by Final/Metrics Score
            sb.AppendLine("<div>");
            sb.AppendLine($"<h3>Ranked by {(results.HasJudgmentScoring ? "Final Score" : "Metrics Score")}</h3>");
            sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Quant</th><th>Score</th></tr></thead><tbody>");
            var rankByScoreHtml = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();
            for (int i = 0; i < rankByScoreHtml.Count; i++)
            {
                var q = rankByScoreHtml[i];
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td>{EscapeHtml(quantDisplay)}</td><td class=\"{GetScoreClass(q.FinalScore)}\">{q.FinalScore:F1}%</td></tr>");
            }
            sb.AppendLine("</tbody></table></div>");

            if (results.HasJudgmentScoring)
            {
                // Ranked by Metrics Score
                sb.AppendLine("<div>");
                sb.AppendLine("<h3>Ranked by Metrics Score</h3>");
                sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Quant</th><th>Score</th></tr></thead><tbody>");
                var rankByMetricsHtml = results.QuantScores.OrderByDescending(q => q.TotalConfidenceScore).ToList();
                for (int i = 0; i < rankByMetricsHtml.Count; i++)
                {
                    var q = rankByMetricsHtml[i];
                    var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                    sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td>{EscapeHtml(quantDisplay)}</td><td class=\"{GetScoreClass(q.TotalConfidenceScore)}\">{q.TotalConfidenceScore:F1}%</td></tr>");
                }
                sb.AppendLine("</tbody></table></div>");

                // Ranked by Judge Score
                sb.AppendLine("<div>");
                sb.AppendLine("<h3>Ranked by Judge Score</h3>");
                sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Quant</th><th>Score</th></tr></thead><tbody>");
                var rankByJudgeHtml = results.QuantScores.Where(q => q.AverageJudgmentScore.HasValue)
                    .OrderByDescending(q => q.AverageJudgmentScore!.Value).ToList();
                for (int i = 0; i < rankByJudgeHtml.Count; i++)
                {
                    var q = rankByJudgeHtml[i];
                    var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                    sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td>{EscapeHtml(quantDisplay)}</td><td class=\"{GetScoreClass(q.AverageJudgmentScore ?? 0)}\">{q.AverageJudgmentScore:F1}%</td></tr>");
                }
                sb.AppendLine("</tbody></table></div>");

                // Ranked by Judge Best Answers
                sb.AppendLine("<div>");
                sb.AppendLine("<h3>Ranked by Best Answers</h3>");
                sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Win %</th><th>B</th><th>A</th><th>=</th></tr></thead><tbody>");
                var rankByBestHtml = results.QuantScores.OrderByDescending(q =>
                {
                    var total = q.BestCount + q.WorstCount + q.TieCount;
                    return total > 0 ? (q.BestCount * 100.0 / total) : 0;
                }).ToList();
                for (int i = 0; i < rankByBestHtml.Count; i++)
                {
                    var q = rankByBestHtml[i];
                    var total = q.BestCount + q.WorstCount + q.TieCount;
                    var winPct = total > 0 ? (q.BestCount * 100.0 / total) : 0;
                    var colorClass = winPct >= 60 ? "score-high" : winPct >= 40 ? "score-mid" : "score-low";
                    sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td class=\"{colorClass}\">{winPct:F0}%</td><td>{q.BestCount}</td><td>{q.WorstCount}</td><td>{q.TieCount}</td></tr>");
                }
                sb.AppendLine("</tbody></table></div>");
            }

            // Ranked by Eval Speed
            sb.AppendLine("<div>");
            sb.AppendLine("<h3>Ranked by Eval Speed</h3>");
            sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Quant</th><th>Speed</th><th>vs Base</th></tr></thead><tbody>");
            var rankByEvalHtml = results.QuantScores.OrderByDescending(q => q.EvalTokensPerSecond).ToList();
            for (int i = 0; i < rankByEvalHtml.Count; i++)
            {
                var q = rankByEvalHtml[i];
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td>{EscapeHtml(quantDisplay)}</td><td class=\"{GetSpeedClass(q.EvalPerformancePercent)}\">{q.EvalTokensPerSecond:F1}</td><td class=\"{GetSpeedClass(q.EvalPerformancePercent)}\">{q.EvalPerformancePercent:F0}%</td></tr>");
            }
            sb.AppendLine("</tbody></table></div>");

            // Ranked by Perplexity Score
            sb.AppendLine("<div>");
            sb.AppendLine("<h3>Ranked by Perplexity</h3>");
            sb.AppendLine("<table><thead><tr><th>#</th><th>Tag</th><th>Quant</th><th>Score</th></tr></thead><tbody>");
            var rankByPerplexityHtml = results.QuantScores.OrderByDescending(q =>
                q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0).ToList();
            for (int i = 0; i < rankByPerplexityHtml.Count; i++)
            {
                var q = rankByPerplexityHtml[i];
                var perplexityScore = q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0;
                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{EscapeHtml(q.Tag)}</td><td>{EscapeHtml(quantDisplay)}</td><td class=\"{GetScoreClass(perplexityScore)}\">{perplexityScore:F1}%</td></tr>");
            }
            sb.AppendLine("</tbody></table></div>");

            sb.AppendLine("</div>"); // End grid

            // Detailed Q&A Section (collapsible)
            sb.AppendLine("<h2>💬 Detailed Questions & Answers</h2>");

            var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);

            foreach (var quantResult in resultsFile.Results.Where(r => !r.IsBase).OrderByDescending(r =>
                results.QuantScores.FirstOrDefault(q => q.Tag == r.Tag)?.FinalScore ?? 0))
            {
                var quantScore = results.QuantScores.FirstOrDefault(q => q.Tag == quantResult.Tag);
                var scoreDisplay = quantScore != null ? $" — Final: {quantScore.FinalScore:F1}%" : "";

                sb.AppendLine($"<button class=\"collapsible\">{EscapeHtml(quantResult.Tag)}{scoreDisplay}</button>");
                sb.AppendLine("<div class=\"content\"><div class=\"content-inner\">");

                foreach (var question in quantResult.QuestionResults)
                {
                    var baseQuestion = baseResult?.QuestionResults.FirstOrDefault(q => q.QuestionId == question.QuestionId);

                    sb.AppendLine("<div class=\"qa-card\">");
                    sb.AppendLine($"<div class=\"qa-question\">Q{question.QuestionId}: {EscapeHtml(question.Question ?? "N/A")}</div>");

                    if (question.Judgment != null)
                    {
                        var bestClass = question.Judgment.BestAnswer == "B" ? "best-b" : question.Judgment.BestAnswer == "A" ? "best-a" : "best-tie";
                        sb.AppendLine($"<span class=\"judgment-badge {bestClass}\">Best: {question.Judgment.BestAnswer ?? "?"}</span>");
                        sb.AppendLine($"<span class=\"judgment-badge badge-score\">Score: {question.Judgment.Score}%</span>");
                        if (!string.IsNullOrWhiteSpace(question.Judgment.Reason))
                        {
                            sb.AppendLine($"<div class=\"reason-text\">💭 {EscapeHtml(question.Judgment.Reason)}</div>");
                        }
                    }

                    sb.AppendLine("<h4>Base Answer (A):</h4>");
                    sb.AppendLine($"<div class=\"qa-answer\">{EscapeHtml(baseQuestion?.Answer ?? "N/A")}</div>");

                    sb.AppendLine("<h4>Quant Answer (B):</h4>");
                    sb.AppendLine($"<div class=\"qa-answer\">{EscapeHtml(question.Answer ?? "N/A")}</div>");

                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div></div>");
            }

            // Footer
            sb.AppendLine($"<p style=\"text-align:center;color:var(--text-secondary);margin-top:40px;padding:20px;border-top:1px solid var(--border-color);\">Generated by <strong>osync qcview</strong> on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");

            // JavaScript for interactivity
            sb.AppendLine("<script>");
            sb.AppendLine(@"
        document.querySelectorAll('.collapsible').forEach(function(btn) {
            btn.addEventListener('click', function() {
                this.classList.toggle('active');
                var content = this.nextElementSibling;
                if (content.style.maxHeight) {
                    content.style.maxHeight = null;
                } else {
                    content.style.maxHeight = content.scrollHeight + 'px';
                }
            });
        });
        function toggleTheme() {
            document.body.classList.toggle('light-theme');
            localStorage.setItem('theme', document.body.classList.contains('light-theme') ? 'light' : 'dark');
        }
        if (localStorage.getItem('theme') === 'light') {
            document.body.classList.add('light-theme');
        }
    ");
            sb.AppendLine("</script>");

            sb.AppendLine("</div></body></html>");

            // Output
            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".html");
            }

            await File.WriteAllTextAsync(outputFile, sb.ToString());
            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]HTML results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Output results as PDF file using QuestPDF
        /// </summary>
        private async Task OutputPdfAsync(QcScoringResults results, QcResultsFile resultsFile)
        {
            // Set QuestPDF license for community use
            QuestPDF.Settings.License = LicenseType.Community;

            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".pdf");
            }

            // Colors
            var headerBg = Colors.Blue.Darken3;
            var headerText = Colors.White;
            var altRowBg = Colors.Grey.Lighten4;

            // Helper function for score colors
            QuestPDF.Infrastructure.Color GetScoreColor(double score) => score >= 80 ? Colors.Green.Darken1 : score >= 60 ? Colors.Orange.Darken1 : Colors.Red.Darken1;

            // Helper function for speed performance colors (>= 100% green, >= 95% orange, < 95% red)
            QuestPDF.Infrastructure.Color GetSpeedColor(double performancePercent) => performancePercent >= 100 ? Colors.Green.Darken1 : performancePercent >= 95 ? Colors.Orange.Darken1 : Colors.Red.Darken1;

            // Sort quantizations by FinalScore for all PDF sections
            var quantScoresForPdf = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();

            await Task.Run(() =>
            {
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(0.8f, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(8));

                        page.Header().Element(header =>
                        {
                            header.Column(col =>
                            {
                                // Title
                                col.Item().Text("Quantization Comparison Results").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);

                                // Header info table for aligned layout
                                col.Item().PaddingTop(8).Table(infoTable =>
                                {
                                    infoTable.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(60);  // Label
                                        c.RelativeColumn(2);   // Value
                                        c.ConstantColumn(60);  // Label
                                        c.RelativeColumn(2);   // Value
                                        c.ConstantColumn(50);  // Label
                                        c.RelativeColumn(1);   // Value
                                    });

                                    // Row 1: Model, Family, Size
                                    infoTable.Cell().Text("Model:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseModelName).FontSize(9);
                                    infoTable.Cell().Text("Family:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseFamily).FontSize(9);
                                    infoTable.Cell().Text("Size:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseParameterSize).FontSize(9);

                                    // Row 2: Test Suite, Judge (if available)
                                    infoTable.Cell().Text("Test Suite:").Bold().FontSize(9);
                                    infoTable.Cell().Text($"{results.TestSuiteName} ({results.TotalQuestions} questions)").FontSize(9);
                                    if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
                                    {
                                        infoTable.Cell().Text("Judge:").Bold().FontSize(9);
                                        infoTable.Cell().Text(results.JudgeModel).FontSize(9);
                                    }
                                    else
                                    {
                                        infoTable.Cell().Text("");
                                        infoTable.Cell().Text("");
                                    }
                                    infoTable.Cell().Text("");
                                    infoTable.Cell().Text("");
                                });

                                // Repository if available
                                if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
                                {
                                    col.Item().PaddingTop(3).Row(r =>
                                    {
                                        r.ConstantItem(60).Text("Repository:").Bold().FontSize(8);
                                        r.RelativeItem().Text(results.RepositoryUrl).FontSize(8).FontColor(Colors.Grey.Darken1);
                                    });
                                }

                                col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            });
                        });

                        page.Content().Element(content =>
                        {
                            content.PaddingVertical(8).Column(col =>
                            {
                                // Base Info Section
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(text =>
                                    {
                                        text.Span("Base Quantization: ").Bold().FontSize(10);
                                        text.Span(results.BaseTag).FontSize(10);
                                    });
                                });
                                col.Item().PaddingBottom(5).Text($"Type: {results.BaseQuantizationType} | Size: {ByteSize.FromBytes(results.BaseDiskSizeBytes)} | Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | Prompt: {results.BasePromptTokensPerSecond:F1} tok/s").FontSize(8);

                                // Main Results Table
                                col.Item().PaddingTop(8).Text("Quantization Quality & Performance").FontSize(11).Bold();
                                col.Item().PaddingTop(4).Table(table =>
                                {
                                    // Define columns with better proportions
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.8f); // Tag - narrower
                                        columns.RelativeColumn(2.2f); // Quant - wider to prevent wrapping
                                        columns.RelativeColumn(1.1f); // Size
                                        if (results.HasJudgmentScoring)
                                        {
                                            columns.RelativeColumn(0.9f); // Final
                                            columns.RelativeColumn(0.9f); // Metrics
                                            columns.RelativeColumn(0.9f); // Judge
                                            columns.RelativeColumn(1.8f); // Best
                                        }
                                        else
                                        {
                                            columns.RelativeColumn(0.9f); // Score
                                        }
                                        columns.RelativeColumn(0.9f); // Token
                                        columns.RelativeColumn(0.9f); // Logprobs
                                        columns.RelativeColumn(0.9f); // Length
                                        columns.RelativeColumn(0.9f); // Perplexity
                                        columns.RelativeColumn(0.8f); // Eval
                                        columns.RelativeColumn(0.8f); // Prompt
                                    });

                                    // Header row
                                    table.Header(header =>
                                    {
                                        header.Cell().Background(headerBg).Padding(3).Text("Tag").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Quant").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Size").FontColor(headerText).Bold().FontSize(7);
                                        if (results.HasJudgmentScoring)
                                        {
                                            header.Cell().Background(headerBg).Padding(3).Text("Final").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Metrics").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Judge").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Best Ans").FontColor(headerText).Bold().FontSize(7);
                                        }
                                        else
                                        {
                                            header.Cell().Background(headerBg).Padding(3).Text("Score").FontColor(headerText).Bold().FontSize(7);
                                        }
                                        header.Cell().Background(headerBg).Padding(3).Text("Token").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Logprobs").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Length").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Perplexity").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Eval").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Prompt").FontColor(headerText).Bold().FontSize(7);
                                    });

                                    // Data rows
                                    int rowIndex = 0;
                                    foreach (var quant in quantScoresForPdf)
                                    {
                                        var rowBg = rowIndex % 2 == 0 ? Colors.White : altRowBg;
                                        var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                                        var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                                        var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                                        var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                                        var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;

                                        table.Cell().Background(rowBg).Padding(2).Text(quant.Tag).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text(quantDisplay).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text(ByteSize.FromBytes(quant.DiskSizeBytes).ToString()).FontSize(7);

                                        if (results.HasJudgmentScoring)
                                        {
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.FinalScore:F1}%").FontColor(GetScoreColor(quant.FinalScore)).FontSize(7);
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.TotalConfidenceScore:F1}%").FontColor(GetScoreColor(quant.TotalConfidenceScore)).FontSize(7);
                                            var judgeScore = quant.AverageJudgmentScore ?? 0;
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.AverageJudgmentScore?.ToString("F1") ?? "N/A"}%").FontColor(GetScoreColor(judgeScore)).FontSize(7);
                                            table.Cell().Background(rowBg).Padding(2).Text(FormatBestStatsPlain(quant)).FontSize(7);
                                        }
                                        else
                                        {
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.TotalConfidenceScore:F1}%").FontColor(GetScoreColor(quant.TotalConfidenceScore)).FontSize(7);
                                        }

                                        table.Cell().Background(rowBg).Padding(2).Text($"{tokenScore:F1}%").FontColor(GetScoreColor(tokenScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{logprobsScore:F1}%").FontColor(GetScoreColor(logprobsScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{lengthScore:F1}%").FontColor(GetScoreColor(lengthScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{perplexityScore:F1}%").FontColor(GetScoreColor(perplexityScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{quant.EvalPerformancePercent:F0}%").FontColor(GetSpeedColor(quant.EvalPerformancePercent)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{quant.PromptPerformancePercent:F0}%").FontColor(GetSpeedColor(quant.PromptPerformancePercent)).FontSize(7);

                                        rowIndex++;
                                    }
                                });

                                // Category Scores Section - use ShowEntire to prevent page break within section
                                if (quantScoresForPdf.Any() && quantScoresForPdf.First().CategoryScores.Any())
                                {
                                    col.Item().ShowEntire().Column(catSection =>
                                    {
                                        catSection.Item().PaddingTop(12).Text("Scores by Category").FontSize(11).Bold();
                                        catSection.Item().PaddingTop(4).Table(catTable =>
                                        {
                                            var categories = quantScoresForPdf.First().CategoryScores.Keys.ToList();

                                            catTable.ColumnsDefinition(c =>
                                            {
                                                c.RelativeColumn(2f); // Tag
                                            foreach (var _ in categories)
                                            {
                                                c.RelativeColumn(1f); // Category Metrics
                                                if (results.HasJudgmentScoring)
                                                    c.RelativeColumn(1f); // Category Judge
                                            }
                                        });

                                        catTable.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).Bold().FontSize(7);
                                            foreach (var cat in categories)
                                            {
                                                h.Cell().Background(headerBg).Padding(2).Text($"{cat}\nMetrics").FontColor(headerText).Bold().FontSize(6);
                                                if (results.HasJudgmentScoring)
                                                    h.Cell().Background(headerBg).Padding(2).Text($"{cat}\nJudge").FontColor(headerText).Bold().FontSize(6);
                                            }
                                        });

                                        int catRowIndex = 0;
                                        foreach (var quant in quantScoresForPdf)
                                        {
                                            var rowBg = catRowIndex % 2 == 0 ? Colors.White : altRowBg;
                                            catTable.Cell().Background(rowBg).Padding(2).Text(quant.Tag).FontSize(7);

                                            foreach (var cat in categories)
                                            {
                                                var metricScore = quant.CategoryScores.GetValueOrDefault(cat);
                                                catTable.Cell().Background(rowBg).Padding(2).Text($"{metricScore:F1}%").FontColor(GetScoreColor(metricScore)).FontSize(7);

                                                if (results.HasJudgmentScoring)
                                                {
                                                    var judgeScore = quant.CategoryJudgmentScores.GetValueOrDefault(cat);
                                                    catTable.Cell().Background(rowBg).Padding(2).Text($"{judgeScore:F1}%").FontColor(GetScoreColor(judgeScore)).FontSize(7);
                                                }
                                            }
                                            catRowIndex++;
                                        }
                                        });
                                    });
                                }

                                // Rankings Section - each ranking table on its own page for clean layout
                                // Rankings are on a separate page to avoid layout issues with main content
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span($"Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                    });

                    // Rankings Page - separate page for rankings tables
                    container.Page(rankingsPage =>
                    {
                        rankingsPage.Size(PageSizes.A4.Landscape());
                        rankingsPage.Margin(0.8f, Unit.Centimetre);
                        rankingsPage.DefaultTextStyle(x => x.FontSize(8));

                        rankingsPage.Header().Column(h =>
                        {
                            h.Item().Text("Rankings").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                            h.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        });

                        rankingsPage.Content().PaddingVertical(8).Column(rankCol =>
                        {
                            // Row 1: Final Score and Eval Speed side by side
                            rankCol.Item().ShowEntire().Row(row1 =>
                            {
                                // By Final/Metrics Score
                                row1.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"By {(results.HasJudgmentScoring ? "Final Score" : "Metrics Score")}").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Score").FontColor(headerText).FontSize(7);
                                        });
                                        for (int i = 0; i < quantScoresForPdf.Count; i++)
                                        {
                                            var q = quantScoresForPdf[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.FinalScore:F1}%").FontColor(GetScoreColor(q.FinalScore)).FontSize(7);
                                        }
                                    });
                                });

                                // By Eval Speed
                                row1.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Eval Speed").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                            c.ConstantColumn(45);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("tok/s").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("vs Base").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByEval = quantScoresForPdf.OrderByDescending(q => q.EvalTokensPerSecond).ToList();
                                        for (int i = 0; i < rankByEval.Count; i++)
                                        {
                                            var q = rankByEval[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.EvalTokensPerSecond:F1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.EvalPerformancePercent:F0}%").FontColor(GetSpeedColor(q.EvalPerformancePercent)).FontSize(7);
                                        }
                                    });
                                });
                            });

                            // Row 2: Perplexity and Prompt Speed side by side
                            rankCol.Item().ShowEntire().PaddingTop(10).Row(row2 =>
                            {
                                // By Perplexity Score
                                row2.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Perplexity Score").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Score").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByPerplexity = quantScoresForPdf.OrderByDescending(q =>
                                            q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0).ToList();
                                        for (int i = 0; i < rankByPerplexity.Count; i++)
                                        {
                                            var q = rankByPerplexity[i];
                                            var perplexityScore = q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0;
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{perplexityScore:F1}%").FontColor(GetScoreColor(perplexityScore)).FontSize(7);
                                        }
                                    });
                                });

                                // By Prompt Speed
                                row2.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Prompt Speed").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                            c.ConstantColumn(45);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("tok/s").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("vs Base").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByPrompt = quantScoresForPdf.OrderByDescending(q => q.PromptTokensPerSecond).ToList();
                                        for (int i = 0; i < rankByPrompt.Count; i++)
                                        {
                                            var q = rankByPrompt[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.PromptTokensPerSecond:F1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.PromptPerformancePercent:F0}%").FontColor(GetSpeedColor(q.PromptPerformancePercent)).FontSize(7);
                                        }
                                    });
                                });
                            });

                            // Row 3: Best Answers (only if judgment scoring)
                            if (results.HasJudgmentScoring)
                            {
                                rankCol.Item().ShowEntire().PaddingTop(10).Row(row3 =>
                                {
                                    row3.RelativeItem().Column(col =>
                                    {
                                        col.Item().Text("By Best Answers").FontSize(10).Bold();
                                        col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                        {
                                            t.ColumnsDefinition(c =>
                                            {
                                                c.ConstantColumn(20);
                                                c.RelativeColumn(2f);
                                                c.RelativeColumn(1.5f);
                                                c.ConstantColumn(50);
                                            });
                                            t.Header(h =>
                                            {
                                                h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Win %").FontColor(headerText).FontSize(7);
                                            });
                                            var rankByBest = quantScoresForPdf.OrderByDescending(q =>
                                            {
                                                var total = q.BestCount + q.WorstCount + q.TieCount;
                                                return total > 0 ? (q.BestCount * 100.0 / total) : 0;
                                            }).ToList();
                                            for (int i = 0; i < rankByBest.Count; i++)
                                            {
                                                var q = rankByBest[i];
                                                var total = q.BestCount + q.WorstCount + q.TieCount;
                                                var winPct = total > 0 ? (q.BestCount * 100.0 / total) : 0;
                                                var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                                t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text($"{winPct:F0}%").FontColor(GetScoreColor(winPct)).FontSize(7);
                                            }
                                        });
                                    });
                                    row3.RelativeItem(); // Empty right side for balance
                                });
                            }
                        });

                        rankingsPage.Footer().AlignCenter().Text(text =>
                        {
                            text.Span($"Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                    });

                    // Q&A Section - each quantization gets pages for its Q&A (full content, no truncation)
                    var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);

                    // Helper for safe text display
                    string SafeText(string? text) => string.IsNullOrEmpty(text) ? "N/A" : text;

                    // All quantizations ordered by score (no filtering)
                    var resultsForPdf = resultsFile.Results.Where(r => !r.IsBase)
                        .OrderByDescending(r => quantScoresForPdf.FirstOrDefault(q => q.Tag == r.Tag)?.FinalScore ?? 0);

                    foreach (var quantResult in resultsForPdf)
                    {
                        var quantScore = quantScoresForPdf.FirstOrDefault(q => q.Tag == quantResult.Tag);

                        container.Page(qaPage =>
                        {
                            qaPage.Size(PageSizes.A4);
                            qaPage.Margin(1, Unit.Centimetre);
                            qaPage.DefaultTextStyle(x => x.FontSize(7));

                            qaPage.Header().Column(h =>
                            {
                                h.Item().Text($"Q&A: {quantResult.Tag}").FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
                                if (quantScore != null)
                                {
                                    h.Item().Text($"Score: {quantScore.FinalScore:F1}% | {quantScore.EnhancedQuantization ?? quantScore.QuantizationType}").FontSize(9);
                                }
                                h.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            });

                            // Flat layout - each question element added directly to content column
                            qaPage.Content().PaddingVertical(3).Column(qaCol =>
                            {
                                foreach (var question in quantResult.QuestionResults)
                                {
                                    var baseQuestion = baseResult?.QuestionResults.FirstOrDefault(q => q.QuestionId == question.QuestionId);

                                    // Question header (simple text, no nesting)
                                    qaCol.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                    qaCol.Item().PaddingTop(3).Text($"Q{question.QuestionId}: {SafeText(question.Question)}").Bold().FontSize(8);

                                    // Judgment line (simple text)
                                    if (question.Judgment != null)
                                    {
                                        var bestLabel = question.Judgment.BestAnswer == "B" ? "✓ Quant Better" :
                                                       question.Judgment.BestAnswer == "A" ? "✗ Base Better" : "= Tied";
                                        qaCol.Item().PaddingTop(2).Text($"[{bestLabel}] Score: {question.Judgment.Score}%").FontSize(7).FontColor(
                                            question.Judgment.BestAnswer == "B" ? Colors.Green.Darken1 :
                                            question.Judgment.BestAnswer == "A" ? Colors.Red.Darken1 : Colors.Orange.Darken1);

                                        if (!string.IsNullOrWhiteSpace(question.Judgment.Reason))
                                        {
                                            qaCol.Item().Text($"Reason: {SafeText(question.Judgment.Reason)}").FontSize(6).Italic().FontColor(Colors.Grey.Darken2);
                                        }
                                    }

                                    // Base Answer - simplified
                                    qaCol.Item().PaddingTop(4).Text("Base (A):").Bold().FontSize(7);
                                    qaCol.Item().Background(Colors.Grey.Lighten4).Padding(3).Text(SafeText(baseQuestion?.Answer)).FontSize(6);

                                    // Quant Answer - simplified
                                    qaCol.Item().PaddingTop(3).Text("Quant (B):").Bold().FontSize(7);
                                    qaCol.Item().Background(Colors.Blue.Lighten5).Padding(3).Text(SafeText(question.Answer)).FontSize(6);
                                }
                            });

                            qaPage.Footer().AlignCenter().Text(text =>
                            {
                                text.Span($"osync qcview | Page ");
                                text.CurrentPageNumber();
                                text.Span("/");
                                text.TotalPages();
                            });
                        });
                    }
                }).GeneratePdf(outputFile);
            });

            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]PDF results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Output results as PDF file with progress tracking
        /// </summary>
        private async Task OutputPdfWithProgressAsync(QcScoringResults results, QcResultsFile resultsFile, ProgressTask progressTask)
        {
            // Set QuestPDF license for community use
            QuestPDF.Settings.License = LicenseType.Community;

            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".pdf");
            }

            // Colors
            var headerBg = Colors.Blue.Darken3;
            var headerText = Colors.White;
            var altRowBg = Colors.Grey.Lighten4;

            // Helper function for score colors
            QuestPDF.Infrastructure.Color GetScoreColor(double score) => score >= 80 ? Colors.Green.Darken1 : score >= 60 ? Colors.Orange.Darken1 : Colors.Red.Darken1;

            // Helper function for speed performance colors (>= 100% green, >= 95% orange, < 95% red)
            QuestPDF.Infrastructure.Color GetSpeedColor(double performancePercent) => performancePercent >= 100 ? Colors.Green.Darken1 : performancePercent >= 95 ? Colors.Orange.Darken1 : Colors.Red.Darken1;

            // Sort quantizations by FinalScore for all PDF sections
            var quantScoresForPdf = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();

            // Get base result for Q&A comparison
            var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);

            progressTask.Description = "[cyan]Generating main tables...[/]";
            progressTask.Increment(5);

            await Task.Run(() =>
            {
                // Helper for safe text display
                string SafeText(string? text) => string.IsNullOrEmpty(text) ? "N/A" : text;

                Document.Create(container =>
                {
                    // Main page with tables
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(0.8f, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(8));

                        page.Header().Element(header =>
                        {
                            header.Column(col =>
                            {
                                col.Item().Text("Quantization Comparison Results").FontSize(18).Bold().FontColor(Colors.Blue.Darken2);
                                col.Item().PaddingTop(8).Table(infoTable =>
                                {
                                    infoTable.ColumnsDefinition(c =>
                                    {
                                        c.ConstantColumn(60);
                                        c.RelativeColumn(2);
                                        c.ConstantColumn(60);
                                        c.RelativeColumn(2);
                                        c.ConstantColumn(50);
                                        c.RelativeColumn(1);
                                    });
                                    infoTable.Cell().Text("Model:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseModelName).FontSize(9);
                                    infoTable.Cell().Text("Family:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseFamily).FontSize(9);
                                    infoTable.Cell().Text("Size:").Bold().FontSize(9);
                                    infoTable.Cell().Text(results.BaseParameterSize).FontSize(9);
                                    infoTable.Cell().Text("Test Suite:").Bold().FontSize(9);
                                    infoTable.Cell().Text($"{results.TestSuiteName} ({results.TotalQuestions} questions)").FontSize(9);
                                    if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
                                    {
                                        infoTable.Cell().Text("Judge:").Bold().FontSize(9);
                                        infoTable.Cell().Text(results.JudgeModel).FontSize(9);
                                    }
                                    else
                                    {
                                        infoTable.Cell().Text("");
                                        infoTable.Cell().Text("");
                                    }
                                    infoTable.Cell().Text("");
                                    infoTable.Cell().Text("");
                                });
                                if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
                                {
                                    col.Item().PaddingTop(3).Row(r =>
                                    {
                                        r.ConstantItem(60).Text("Repository:").Bold().FontSize(8);
                                        r.RelativeItem().Text(results.RepositoryUrl).FontSize(8).FontColor(Colors.Grey.Darken1);
                                    });
                                }
                                col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            });
                        });

                        page.Content().Element(content =>
                        {
                            content.PaddingVertical(8).Column(col =>
                            {
                                col.Item().Row(row =>
                                {
                                    row.RelativeItem().Text(text =>
                                    {
                                        text.Span("Base Quantization: ").Bold().FontSize(10);
                                        text.Span(results.BaseTag).FontSize(10);
                                    });
                                });
                                col.Item().PaddingBottom(5).Text($"Type: {results.BaseQuantizationType} | Size: {ByteSize.FromBytes(results.BaseDiskSizeBytes)} | Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | Prompt: {results.BasePromptTokensPerSecond:F1} tok/s").FontSize(8);

                                // Main Results Table
                                col.Item().PaddingTop(8).Text("Quantization Quality & Performance").FontSize(11).Bold();
                                col.Item().PaddingTop(4).Table(table =>
                                {
                                    table.ColumnsDefinition(columns =>
                                    {
                                        columns.RelativeColumn(1.8f);
                                        columns.RelativeColumn(2.2f);
                                        columns.RelativeColumn(1.1f);
                                        if (results.HasJudgmentScoring)
                                        {
                                            columns.RelativeColumn(0.9f);
                                            columns.RelativeColumn(0.9f);
                                            columns.RelativeColumn(0.9f);
                                            columns.RelativeColumn(1.8f);
                                        }
                                        else
                                        {
                                            columns.RelativeColumn(0.9f);
                                        }
                                        columns.RelativeColumn(0.9f);
                                        columns.RelativeColumn(0.9f);
                                        columns.RelativeColumn(0.9f);
                                        columns.RelativeColumn(0.9f);
                                        columns.RelativeColumn(0.8f);
                                        columns.RelativeColumn(0.8f);
                                    });

                                    table.Header(header =>
                                    {
                                        header.Cell().Background(headerBg).Padding(3).Text("Tag").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Quant").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Size").FontColor(headerText).Bold().FontSize(7);
                                        if (results.HasJudgmentScoring)
                                        {
                                            header.Cell().Background(headerBg).Padding(3).Text("Final").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Metrics").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Judge").FontColor(headerText).Bold().FontSize(7);
                                            header.Cell().Background(headerBg).Padding(3).Text("Best Ans").FontColor(headerText).Bold().FontSize(7);
                                        }
                                        else
                                        {
                                            header.Cell().Background(headerBg).Padding(3).Text("Score").FontColor(headerText).Bold().FontSize(7);
                                        }
                                        header.Cell().Background(headerBg).Padding(3).Text("Token").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Logprobs").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Length").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Perplexity").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Eval").FontColor(headerText).Bold().FontSize(7);
                                        header.Cell().Background(headerBg).Padding(3).Text("Prompt").FontColor(headerText).Bold().FontSize(7);
                                    });

                                    int rowIndex = 0;
                                    foreach (var quant in quantScoresForPdf)
                                    {
                                        var rowBg = rowIndex % 2 == 0 ? Colors.White : altRowBg;
                                        var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                                        var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                                        var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                                        var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                                        var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;

                                        table.Cell().Background(rowBg).Padding(2).Text(quant.Tag).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text(quantDisplay).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text(ByteSize.FromBytes(quant.DiskSizeBytes).ToString()).FontSize(7);

                                        if (results.HasJudgmentScoring)
                                        {
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.FinalScore:F1}%").FontColor(GetScoreColor(quant.FinalScore)).FontSize(7);
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.TotalConfidenceScore:F1}%").FontColor(GetScoreColor(quant.TotalConfidenceScore)).FontSize(7);
                                            var judgeScore = quant.AverageJudgmentScore ?? 0;
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.AverageJudgmentScore?.ToString("F1") ?? "N/A"}%").FontColor(GetScoreColor(judgeScore)).FontSize(7);
                                            table.Cell().Background(rowBg).Padding(2).Text(FormatBestStatsPlain(quant)).FontSize(7);
                                        }
                                        else
                                        {
                                            table.Cell().Background(rowBg).Padding(2).Text($"{quant.TotalConfidenceScore:F1}%").FontColor(GetScoreColor(quant.TotalConfidenceScore)).FontSize(7);
                                        }

                                        table.Cell().Background(rowBg).Padding(2).Text($"{tokenScore:F1}%").FontColor(GetScoreColor(tokenScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{logprobsScore:F1}%").FontColor(GetScoreColor(logprobsScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{lengthScore:F1}%").FontColor(GetScoreColor(lengthScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{perplexityScore:F1}%").FontColor(GetScoreColor(perplexityScore)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{quant.EvalPerformancePercent:F0}%").FontColor(GetSpeedColor(quant.EvalPerformancePercent)).FontSize(7);
                                        table.Cell().Background(rowBg).Padding(2).Text($"{quant.PromptPerformancePercent:F0}%").FontColor(GetSpeedColor(quant.PromptPerformancePercent)).FontSize(7);

                                        rowIndex++;
                                    }
                                });

                                // Category Scores Section - use ShowEntire to prevent page break within section
                                if (quantScoresForPdf.Any() && quantScoresForPdf.First().CategoryScores.Any())
                                {
                                    col.Item().ShowEntire().Column(catSection =>
                                    {
                                        catSection.Item().PaddingTop(12).Text("Scores by Category").FontSize(11).Bold();
                                        catSection.Item().PaddingTop(4).Table(catTable =>
                                        {
                                            var categories = quantScoresForPdf.First().CategoryScores.Keys.ToList();

                                            catTable.ColumnsDefinition(c =>
                                            {
                                                c.RelativeColumn(2f);
                                            foreach (var _ in categories)
                                            {
                                                c.RelativeColumn(1f);
                                                if (results.HasJudgmentScoring)
                                                    c.RelativeColumn(1f);
                                            }
                                        });

                                        catTable.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).Bold().FontSize(7);
                                            foreach (var cat in categories)
                                            {
                                                h.Cell().Background(headerBg).Padding(2).Text($"{cat}\nMetrics").FontColor(headerText).Bold().FontSize(6);
                                                if (results.HasJudgmentScoring)
                                                    h.Cell().Background(headerBg).Padding(2).Text($"{cat}\nJudge").FontColor(headerText).Bold().FontSize(6);
                                            }
                                        });

                                        int catRowIndex = 0;
                                        foreach (var quant in quantScoresForPdf)
                                        {
                                            var rowBg = catRowIndex % 2 == 0 ? Colors.White : altRowBg;
                                            catTable.Cell().Background(rowBg).Padding(2).Text(quant.Tag).FontSize(7);

                                            foreach (var cat in categories)
                                            {
                                                var metricScore = quant.CategoryScores.GetValueOrDefault(cat);
                                                catTable.Cell().Background(rowBg).Padding(2).Text($"{metricScore:F1}%").FontColor(GetScoreColor(metricScore)).FontSize(7);

                                                if (results.HasJudgmentScoring)
                                                {
                                                    var judgeCatScore = quant.CategoryJudgmentScores.GetValueOrDefault(cat);
                                                    catTable.Cell().Background(rowBg).Padding(2).Text($"{judgeCatScore:F1}%").FontColor(GetScoreColor(judgeCatScore)).FontSize(7);
                                                }
                                            }
                                            catRowIndex++;
                                        }
                                        });
                                    });
                                }
                            });
                        });

                        page.Footer().AlignCenter().Text(text =>
                        {
                            text.Span($"Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                    });

                    // Rankings Page
                    container.Page(rankingsPage =>
                    {
                        rankingsPage.Size(PageSizes.A4.Landscape());
                        rankingsPage.Margin(0.8f, Unit.Centimetre);
                        rankingsPage.DefaultTextStyle(x => x.FontSize(8));

                        rankingsPage.Header().Column(h =>
                        {
                            h.Item().Text("Rankings").FontSize(14).Bold().FontColor(Colors.Blue.Darken2);
                            h.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                        });

                        rankingsPage.Content().PaddingVertical(8).Column(rankCol =>
                        {
                            // Row 1: Final Score and Eval Speed side by side
                            rankCol.Item().ShowEntire().Row(row1 =>
                            {
                                // By Final/Metrics Score
                                row1.RelativeItem().Column(col =>
                                {
                                    col.Item().Text($"By {(results.HasJudgmentScoring ? "Final Score" : "Metrics Score")}").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Score").FontColor(headerText).FontSize(7);
                                        });
                                        for (int i = 0; i < quantScoresForPdf.Count; i++)
                                        {
                                            var q = quantScoresForPdf[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.FinalScore:F1}%").FontColor(GetScoreColor(q.FinalScore)).FontSize(7);
                                        }
                                    });
                                });

                                // By Eval Speed
                                row1.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Eval Speed").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                            c.ConstantColumn(45);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("tok/s").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("vs Base").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByEval = quantScoresForPdf.OrderByDescending(q => q.EvalTokensPerSecond).ToList();
                                        for (int i = 0; i < rankByEval.Count; i++)
                                        {
                                            var q = rankByEval[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.EvalTokensPerSecond:F1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.EvalPerformancePercent:F0}%").FontColor(GetSpeedColor(q.EvalPerformancePercent)).FontSize(7);
                                        }
                                    });
                                });
                            });

                            // Row 2: Perplexity and Prompt Speed side by side
                            rankCol.Item().ShowEntire().PaddingTop(10).Row(row2 =>
                            {
                                // By Perplexity Score
                                row2.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Perplexity Score").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Score").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByPerplexity = quantScoresForPdf.OrderByDescending(q =>
                                            q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0).ToList();
                                        for (int i = 0; i < rankByPerplexity.Count; i++)
                                        {
                                            var q = rankByPerplexity[i];
                                            var perplexityScore = q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0;
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{perplexityScore:F1}%").FontColor(GetScoreColor(perplexityScore)).FontSize(7);
                                        }
                                    });
                                });

                                // By Prompt Speed
                                row2.RelativeItem().Column(col =>
                                {
                                    col.Item().Text("By Prompt Speed").FontSize(10).Bold();
                                    col.Item().PaddingTop(2).Table(t =>
                                    {
                                        t.ColumnsDefinition(c =>
                                        {
                                            c.ConstantColumn(20);
                                            c.RelativeColumn(2f);
                                            c.RelativeColumn(1.5f);
                                            c.ConstantColumn(50);
                                            c.ConstantColumn(45);
                                        });
                                        t.Header(h =>
                                        {
                                            h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("tok/s").FontColor(headerText).FontSize(7);
                                            h.Cell().Background(headerBg).Padding(2).Text("vs Base").FontColor(headerText).FontSize(7);
                                        });
                                        var rankByPrompt = quantScoresForPdf.OrderByDescending(q => q.PromptTokensPerSecond).ToList();
                                        for (int i = 0; i < rankByPrompt.Count; i++)
                                        {
                                            var q = rankByPrompt[i];
                                            var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                            var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                            t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.PromptTokensPerSecond:F1}").FontSize(7);
                                            t.Cell().Background(bg).Padding(2).Text($"{q.PromptPerformancePercent:F0}%").FontColor(GetSpeedColor(q.PromptPerformancePercent)).FontSize(7);
                                        }
                                    });
                                });
                            });

                            // Row 3: Best Answers (only if judgment scoring)
                            if (results.HasJudgmentScoring)
                            {
                                rankCol.Item().ShowEntire().PaddingTop(10).Row(row3 =>
                                {
                                    row3.RelativeItem().Column(col =>
                                    {
                                        col.Item().Text("By Best Answers").FontSize(10).Bold();
                                        col.Item().PaddingTop(2).PaddingRight(10).Table(t =>
                                        {
                                            t.ColumnsDefinition(c =>
                                            {
                                                c.ConstantColumn(20);
                                                c.RelativeColumn(2f);
                                                c.RelativeColumn(1.5f);
                                                c.ConstantColumn(50);
                                            });
                                            t.Header(h =>
                                            {
                                                h.Cell().Background(headerBg).Padding(2).Text("#").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Tag").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Quant").FontColor(headerText).FontSize(7);
                                                h.Cell().Background(headerBg).Padding(2).Text("Win %").FontColor(headerText).FontSize(7);
                                            });
                                            var rankByBest = quantScoresForPdf.OrderByDescending(q =>
                                            {
                                                var total = q.BestCount + q.WorstCount + q.TieCount;
                                                return total > 0 ? (q.BestCount * 100.0 / total) : 0;
                                            }).ToList();
                                            for (int i = 0; i < rankByBest.Count; i++)
                                            {
                                                var q = rankByBest[i];
                                                var total = q.BestCount + q.WorstCount + q.TieCount;
                                                var winPct = total > 0 ? (q.BestCount * 100.0 / total) : 0;
                                                var bg = i % 2 == 0 ? Colors.White : altRowBg;
                                                var quantDisplay = q.EnhancedQuantization ?? q.QuantizationType;
                                                t.Cell().Background(bg).Padding(2).Text($"{i + 1}").FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text(q.Tag).FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text(quantDisplay).FontSize(7);
                                                t.Cell().Background(bg).Padding(2).Text($"{winPct:F0}%").FontColor(GetScoreColor(winPct)).FontSize(7);
                                            }
                                        });
                                    });
                                    row3.RelativeItem(); // Empty right side for balance
                                });
                            }
                        });

                        rankingsPage.Footer().AlignCenter().Text(text =>
                        {
                            text.Span($"Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Page ");
                            text.CurrentPageNumber();
                            text.Span(" of ");
                            text.TotalPages();
                        });
                    });

                    // Q&A Pages
                    var resultsForQA = resultsFile.Results.Where(r => !r.IsBase)
                        .OrderByDescending(r => quantScoresForPdf.FirstOrDefault(q => q.Tag == r.Tag)?.FinalScore ?? 0);

                    foreach (var quantResult in resultsForQA)
                    {
                        var quantScore = quantScoresForPdf.FirstOrDefault(q => q.Tag == quantResult.Tag);

                        container.Page(qaPage =>
                        {
                            qaPage.Size(PageSizes.A4);
                            qaPage.Margin(1, Unit.Centimetre);
                            qaPage.DefaultTextStyle(x => x.FontSize(7));

                            qaPage.Header().Column(h =>
                            {
                                h.Item().Text($"Q&A: {quantResult.Tag}").FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
                                if (quantScore != null)
                                {
                                    h.Item().Text($"Score: {quantScore.FinalScore:F1}% | {quantScore.EnhancedQuantization ?? quantScore.QuantizationType}").FontSize(9);
                                }
                                h.Item().PaddingTop(3).LineHorizontal(1).LineColor(Colors.Grey.Medium);
                            });

                            qaPage.Content().PaddingVertical(3).Column(qaCol =>
                            {
                                foreach (var question in quantResult.QuestionResults)
                                {
                                    var baseQuestion = baseResult?.QuestionResults.FirstOrDefault(q => q.QuestionId == question.QuestionId);

                                    qaCol.Item().PaddingTop(6).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten2);
                                    qaCol.Item().PaddingTop(3).Text($"Q{question.QuestionId}: {SafeText(question.Question)}").Bold().FontSize(8);

                                    if (question.Judgment != null)
                                    {
                                        var bestLabel = question.Judgment.BestAnswer == "B" ? "✓ Quant Better" :
                                                       question.Judgment.BestAnswer == "A" ? "✗ Base Better" : "= Tied";
                                        qaCol.Item().PaddingTop(2).Text($"[{bestLabel}] Score: {question.Judgment.Score}%").FontSize(7).FontColor(
                                            question.Judgment.BestAnswer == "B" ? Colors.Green.Darken1 :
                                            question.Judgment.BestAnswer == "A" ? Colors.Red.Darken1 : Colors.Orange.Darken1);

                                        if (!string.IsNullOrWhiteSpace(question.Judgment.Reason))
                                        {
                                            qaCol.Item().Text($"Reason: {SafeText(question.Judgment.Reason)}").FontSize(6).Italic().FontColor(Colors.Grey.Darken2);
                                        }
                                    }

                                    qaCol.Item().PaddingTop(4).Text("Base (A):").Bold().FontSize(7);
                                    qaCol.Item().Background(Colors.Grey.Lighten4).Padding(3).Text(SafeText(baseQuestion?.Answer)).FontSize(6);

                                    qaCol.Item().PaddingTop(3).Text("Quant (B):").Bold().FontSize(7);
                                    qaCol.Item().Background(Colors.Blue.Lighten5).Padding(3).Text(SafeText(question.Answer)).FontSize(6);
                                }
                            });

                            qaPage.Footer().AlignCenter().Text(text =>
                            {
                                text.Span($"osync qcview | Page ");
                                text.CurrentPageNumber();
                                text.Span("/");
                                text.TotalPages();
                            });
                        });
                    }
                }).GeneratePdf(outputFile);
            });

            var fileInfo = new FileInfo(outputFile);
            progressTask.Description = $"[green]PDF saved: {ByteSize.FromBytes(fileInfo.Length)}[/]";
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return System.Net.WebUtility.HtmlEncode(text);
        }

        private static string GetScoreClass(double score)
        {
            return score >= 80 ? "score-high" : score >= 60 ? "score-mid" : "score-low";
        }

        /// <summary>
        /// Get CSS class for speed comparison (green if >= 100%, orange if 95-100%, red if &lt; 95%)
        /// </summary>
        private static string GetSpeedClass(double performancePercent)
        {
            return performancePercent >= 100 ? "speed-high" : performancePercent >= 95 ? "speed-mid" : "speed-low";
        }

        private string FormatBestStatsHtml(QuantScoreResult quant)
        {
            var total = quant.BestCount + quant.WorstCount + quant.TieCount;
            if (total == 0) return "N/A";

            var bestPercent = (quant.BestCount * 100.0) / total;
            var color = bestPercent >= 60 ? "#28a745" : bestPercent >= 40 ? "#ffc107" : "#dc3545";

            return $"<span style=\"color:{color};font-weight:bold\">{bestPercent:F0}%</span> <small>(B:{quant.BestCount} A:{quant.WorstCount} =:{quant.TieCount})</small>";
        }
    }
}
