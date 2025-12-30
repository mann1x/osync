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
                    DisplayTable(scoringResults);
                }

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                AnsiConsole.MarkupLine($"[dim]{ex.StackTrace}[/]");
                return 1;
            }
        }

        /// <summary>
        /// Display results as formatted table in console
        /// </summary>
        private void DisplayTable(QcScoringResults results)
        {
            // Header
            var panel = new Panel($"[bold cyan]Quantization Comparison Results[/]\n" +
                                 $"Model: [yellow]{results.BaseModelName}[/] | " +
                                 $"Family: [yellow]{results.BaseFamily}[/] | " +
                                 $"Size: [yellow]{results.BaseParameterSize}[/]\n" +
                                 $"Test Suite: [yellow]{results.TestSuiteName}[/] ({results.TotalQuestions} questions)")
            {
                Border = BoxBorder.Double,
                Padding = new Padding(1, 0)
            };
            AnsiConsole.Write(panel);
            AnsiConsole.WriteLine();

            // Base model info
            var baseInfo = new Panel($"[bold]Base Quantization:[/] [cyan]{results.BaseTag}[/]\n" +
                                    $"Type: {results.BaseQuantizationType} | " +
                                    $"Size: {ByteSize.FromBytes(results.BaseDiskSizeBytes).ToString()} | " +
                                    $"Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | " +
                                    $"Prompt: {results.BasePromptTokensPerSecond:F1} tok/s")
            {
                Border = BoxBorder.Rounded
            };
            AnsiConsole.Write(baseInfo);
            AnsiConsole.WriteLine();

            // Test options
            DisplayTestOptions(results.Options);
            AnsiConsole.WriteLine();

            // Main results table
            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.Title = new TableTitle("[bold yellow]Quantization Quality & Performance[/]");

            // Add columns
            table.AddColumn(new TableColumn("[bold]Tag[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Quant[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Overall\nScore[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Token\nSimilarity[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Logprobs\nDivergence[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Length\nConsistency[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Perplexity[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Eval Speed\n(tok/s)[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Eval vs\nBase[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Prompt Speed\n(tok/s)[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Prompt vs\nBase[/]").Centered());

            // Sort by overall score descending
            var sortedResults = results.QuantScores.OrderByDescending(q => q.TotalConfidenceScore).ToList();

            foreach (var quant in sortedResults)
            {
                var sizeStr = ByteSize.FromBytes(quant.DiskSizeBytes).ToString();
                var overallScore = FormatScore(quant.TotalConfidenceScore);

                // Calculate component scores (average across all questions)
                var tokenScore = quant.QuestionScores.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.TokenSimilarityScore))
                    : "N/A";
                var logprobsScore = quant.QuestionScores.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.LogprobsDivergenceScore))
                    : "N/A";
                var lengthScore = quant.QuestionScores.Count > 0
                    ? FormatScore(quant.QuestionScores.Average(q => q.LengthConsistencyScore))
                    : "N/A";
                var perplexityScore = quant.QuestionScores.Count > 0
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

                table.AddRow(
                    quant.Tag,
                    quant.QuantizationType,
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

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

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
            table.Title = new TableTitle("[bold yellow]Scores by Category[/]");

            // Add columns
            table.AddColumn(new TableColumn("[bold]Tag[/]").LeftAligned());
            foreach (var category in categories)
            {
                table.AddColumn(new TableColumn($"[bold]{category}[/]").Centered());
            }

            // Sort by overall score
            var sortedResults = results.QuantScores.OrderByDescending(q => q.TotalConfidenceScore).ToList();

            foreach (var quant in sortedResults)
            {
                var row = new List<string> { quant.Tag };

                foreach (var category in categories)
                {
                    if (quant.CategoryScores.TryGetValue(category, out var score))
                    {
                        row.Add(FormatScore(score));
                    }
                    else
                    {
                        row.Add("N/A");
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
        /// Green (90-100%): Excellent quality preservation
        /// Lime (80-90%): Very good quality
        /// Yellow (70-80%): Good quality
        /// Orange (50-70%): Moderate quality loss
        /// Red (below 50%): Significant quality degradation
        /// </summary>
        private string GetScoreColor(double score)
        {
            if (score >= 90) return "green";
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
            if (percent > 100) color = "green";
            else if (percent < 100) color = "yellow";

            var arrow = percent > 100 ? "↑" : (percent < 100 ? "↓" : "=");
            return $"[{color}]{percent:F0}% {arrow}[/]";
        }

        /// <summary>
        /// Display results as JSON (console or file)
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
                // Output to console
                var panel = new Panel(json)
                {
                    Header = new PanelHeader("[yellow]JSON Results[/]"),
                    Border = BoxBorder.Rounded
                };
                AnsiConsole.Write(panel);
            }
            else
            {
                // Output to file
                await File.WriteAllTextAsync(_args.OutputFile, json);
                AnsiConsole.MarkupLine($"[green]JSON results saved to: {_args.OutputFile}[/]");
            }
        }
    }
}
