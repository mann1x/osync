// Ignore Spelling: osync

using System.Text;
using System.Text.Json;
using ByteSizeLib;
using iText.Kernel.Pdf;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Layout;
using iText.Layout.Element;
using iText.Layout.Properties;
using iText.IO.Font.Constants;
using Spectre.Console;
using iTextDocument = iText.Layout.Document;
using iTextTable = iText.Layout.Element.Table;
using iTextCell = iText.Layout.Element.Cell;
using iTextParagraph = iText.Layout.Element.Paragraph;
using iTextText = iText.Layout.Element.Text;
using iTextColor = iText.Kernel.Colors.Color;
using SpectreTable = Spectre.Console.Table;
using Path = System.IO.Path;

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

                // Check file access BEFORE starting progress bar to avoid concurrent display errors
                if (isFileOutput)
                {
                    var outputFile = _args.OutputFile;
                    if (string.IsNullOrWhiteSpace(outputFile))
                    {
                        // Determine default extension based on format
                        var ext = format switch
                        {
                            "json" => ".json",
                            "md" or "markdown" => ".md",
                            "html" => ".html",
                            "pdf" => ".pdf",
                            _ => ".txt"
                        };
                        outputFile = Path.ChangeExtension(_args.FileName, ext);
                    }
                    if (!CheckOutputFileAccess(outputFile))
                        return 0;
                }

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
                            // skipFileAccessCheck: true because we already checked before starting progress bar
                            switch (format)
                            {
                                case "json":
                                    mainTask.Description = "[cyan]Writing JSON...[/]";
                                    await DisplayJsonAsync(scoringResults, skipFileAccessCheck: true);
                                    break;
                                case "md":
                                case "markdown":
                                    mainTask.Description = "[cyan]Generating Markdown...[/]";
                                    await OutputMarkdownAsync(scoringResults, resultsFile, skipFileAccessCheck: true);
                                    break;
                                case "html":
                                    mainTask.Description = "[cyan]Generating HTML...[/]";
                                    await OutputHtmlAsync(scoringResults, resultsFile, skipFileAccessCheck: true);
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
                // Check file access before proceeding
                if (!CheckOutputFileAccess(_args.OutputFile))
                    return;

                // Output to file as plain text
                var sb = new StringBuilder();
                sb.AppendLine("Quantization Comparison Results");
                sb.AppendLine($"Model: {results.BaseModelName} | Family: {results.BaseFamily} | Size: {results.BaseParameterSize}");
                sb.AppendLine($"Test Suite: {results.TestSuiteName} ({results.TotalQuestions} questions)");

                if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
                {
                    var judgeProviderText = FormatCloudProviderPdf(results.JudgeProvider, results.JudgeApiVersion);
                    var judgeProviderSuffix = judgeProviderText != null ? $" {judgeProviderText}" : "";
                    sb.AppendLine($"Judge Model: {results.JudgeModel}{judgeProviderSuffix} (50% metrics + 50% judgment)");
                    if (!string.IsNullOrEmpty(results.JudgeModelBestAnswer) && results.JudgeModelBestAnswer != results.JudgeModel)
                    {
                        var bestProviderText = FormatCloudProviderPdf(results.JudgeBestProvider, results.JudgeBestApiVersion);
                        var bestProviderSuffix = bestProviderText != null ? $" {bestProviderText}" : "";
                        sb.AppendLine($"Best Answer Judge: {results.JudgeModelBestAnswer}{bestProviderSuffix}");
                    }
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
                var judgeProviderBadge = FormatCloudProviderConsole(results.JudgeProvider, results.JudgeApiVersion);
                var judgeProviderSuffix = judgeProviderBadge != null ? $" {judgeProviderBadge}" : "";
                headerText += $"\nJudge Model: [magenta]{Markup.Escape(results.JudgeModel)}[/]{judgeProviderSuffix} [dim](50% metrics + 50% judgment)[/]";
                // Show best answer judge if different from similarity judge
                if (!string.IsNullOrEmpty(results.JudgeModelBestAnswer) && results.JudgeModelBestAnswer != results.JudgeModel)
                {
                    var bestProviderBadge = FormatCloudProviderConsole(results.JudgeBestProvider, results.JudgeBestApiVersion);
                    var bestProviderSuffix = bestProviderBadge != null ? $" {bestProviderBadge}" : "";
                    headerText += $"\nBest Answer Judge: [magenta]{Markup.Escape(results.JudgeModelBestAnswer)}[/]{bestProviderSuffix}";
                }
            }

            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                headerText += $"\nRepository: [blue]{Markup.Escape(results.RepositoryUrl)}[/]";
            }

            // Version information
            var versionParts = new List<string>();
            if (!string.IsNullOrEmpty(results.OsyncVersion))
                versionParts.Add($"osync {results.OsyncVersion}");
            if (!string.IsNullOrEmpty(results.OllamaVersion))
                versionParts.Add($"Ollama {results.OllamaVersion}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeVersion) && results.OllamaJudgeVersion != results.OllamaVersion)
                versionParts.Add($"Judge Ollama {results.OllamaJudgeVersion}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeBestAnswerVersion) && results.OllamaJudgeBestAnswerVersion != results.OllamaJudgeVersion)
                versionParts.Add($"Best Judge Ollama {results.OllamaJudgeBestAnswerVersion}");
            if (versionParts.Count > 0)
            {
                headerText += $"\n[dim]Versions: {string.Join(" | ", versionParts)}[/]";
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
            var table = new SpectreTable();
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

            var table = new SpectreTable();
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
        private async Task DisplayJsonAsync(QcScoringResults results, bool skipFileAccessCheck = false)
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
                // Check file access before proceeding (skip if already checked)
                if (!skipFileAccessCheck && !CheckOutputFileAccess(_args.OutputFile))
                    return;

                // Output to file
                await File.WriteAllTextAsync(_args.OutputFile, json);
                var fileInfo = new FileInfo(_args.OutputFile);
                AnsiConsole.MarkupLine($"[green]JSON results saved to: {_args.OutputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
            }
        }

        /// <summary>
        /// Output results as Markdown file
        /// </summary>
        private async Task OutputMarkdownAsync(QcScoringResults results, QcResultsFile resultsFile, bool skipFileAccessCheck = false)
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
                var judgeProviderBadge = FormatCloudProviderMarkdown(results.JudgeProvider, results.JudgeApiVersion);
                var judgeProviderSuffix = judgeProviderBadge != null ? $" {judgeProviderBadge}" : "";
                sb.AppendLine($"**Judge Model:** {results.JudgeModel}{judgeProviderSuffix} (50% metrics + 50% judgment)  ");
                if (!string.IsNullOrEmpty(results.JudgeModelBestAnswer) && results.JudgeModelBestAnswer != results.JudgeModel)
                {
                    var bestProviderBadge = FormatCloudProviderMarkdown(results.JudgeBestProvider, results.JudgeBestApiVersion);
                    var bestProviderSuffix = bestProviderBadge != null ? $" {bestProviderBadge}" : "";
                    sb.AppendLine($"**Best Answer Judge:** {results.JudgeModelBestAnswer}{bestProviderSuffix}  ");
                }
            }

            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                sb.AppendLine($"**Repository:** [{results.RepositoryUrl}]({results.RepositoryUrl})  ");
            }

            // Version information
            var versionParts = new List<string>();
            if (!string.IsNullOrEmpty(results.OsyncVersion))
                versionParts.Add($"osync {results.OsyncVersion}");
            if (!string.IsNullOrEmpty(results.OllamaVersion))
                versionParts.Add($"Ollama {results.OllamaVersion}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeVersion) && results.OllamaJudgeVersion != results.OllamaVersion)
                versionParts.Add($"Judge Ollama {results.OllamaJudgeVersion}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeBestAnswerVersion) && results.OllamaJudgeBestAnswerVersion != results.OllamaJudgeVersion)
                versionParts.Add($"Best Judge Ollama {results.OllamaJudgeBestAnswerVersion}");
            if (versionParts.Count > 0)
            {
                sb.AppendLine($"**Versions:** {string.Join(" | ", versionParts)}  ");
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

            // Check file access before proceeding (skip if already checked)
            if (!skipFileAccessCheck && !CheckOutputFileAccess(outputFile))
                return;

            await File.WriteAllTextAsync(outputFile, sb.ToString());
            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]Markdown results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Output results as HTML file with interactive features
        /// </summary>
        private async Task OutputHtmlAsync(QcScoringResults results, QcResultsFile resultsFile, bool skipFileAccessCheck = false)
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
        .cloud-provider-badge {
            display: inline-block;
            padding: 2px 8px;
            background: linear-gradient(135deg, rgba(147, 112, 219, 0.2), rgba(88, 166, 255, 0.2));
            border: 1px solid rgba(147, 112, 219, 0.4);
            border-radius: 12px;
            font-size: 0.75em;
            color: var(--accent-purple);
            font-weight: 500;
            margin-left: 8px;
        }
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
                var judgeProviderBadge = FormatCloudProviderHtml(results.JudgeProvider, results.JudgeApiVersion);
                var judgeModelDisplay = judgeProviderBadge != null
                    ? $"{EscapeHtml(results.JudgeModel)} {judgeProviderBadge}"
                    : EscapeHtml(results.JudgeModel);
                sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Judge Model</span><span class=\"info-value\">{judgeModelDisplay}</span></div>");

                if (!string.IsNullOrEmpty(results.JudgeModelBestAnswer) && results.JudgeModelBestAnswer != results.JudgeModel)
                {
                    var bestProviderBadge = FormatCloudProviderHtml(results.JudgeBestProvider, results.JudgeBestApiVersion);
                    var bestModelDisplay = bestProviderBadge != null
                        ? $"{EscapeHtml(results.JudgeModelBestAnswer)} {bestProviderBadge}"
                        : EscapeHtml(results.JudgeModelBestAnswer);
                    sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Best Answer Judge</span><span class=\"info-value\">{bestModelDisplay}</span></div>");
                }
            }

            sb.AppendLine("</div>"); // Close info-grid

            // Repository as full-width row below the grid
            if (!string.IsNullOrWhiteSpace(results.RepositoryUrl))
            {
                sb.AppendLine($"<div class=\"info-item\" style=\"grid-column: 1 / -1; margin-top: 0.5rem;\"><span class=\"info-label\">Repository</span><span class=\"info-value\"><a href=\"{EscapeHtml(results.RepositoryUrl)}\" target=\"_blank\">{EscapeHtml(results.RepositoryUrl)}</a></span></div>");
            }

            // Version information
            var htmlVersionParts = new List<string>();
            if (!string.IsNullOrEmpty(results.OsyncVersion))
                htmlVersionParts.Add($"osync {EscapeHtml(results.OsyncVersion)}");
            if (!string.IsNullOrEmpty(results.OllamaVersion))
                htmlVersionParts.Add($"Ollama {EscapeHtml(results.OllamaVersion)}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeVersion) && results.OllamaJudgeVersion != results.OllamaVersion)
                htmlVersionParts.Add($"Judge Ollama {EscapeHtml(results.OllamaJudgeVersion)}");
            if (!string.IsNullOrEmpty(results.OllamaJudgeBestAnswerVersion) && results.OllamaJudgeBestAnswerVersion != results.OllamaJudgeVersion)
                htmlVersionParts.Add($"Best Judge Ollama {EscapeHtml(results.OllamaJudgeBestAnswerVersion)}");
            if (htmlVersionParts.Count > 0)
            {
                sb.AppendLine($"<div class=\"info-item\" style=\"grid-column: 1 / -1; margin-top: 0.5rem;\"><span class=\"info-label\">Versions</span><span class=\"info-value\" style=\"color: var(--text-secondary);\">{string.Join(" | ", htmlVersionParts)}</span></div>");
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
                            sb.AppendLine($"<div class=\"reason-text\">💭 [Similarity] {EscapeHtml(question.Judgment.Reason)}</div>");
                        }
                        if (!string.IsNullOrWhiteSpace(question.Judgment.ReasonBestAnswer))
                        {
                            sb.AppendLine($"<div class=\"reason-text\">💭 [BestAnswer] {EscapeHtml(question.Judgment.ReasonBestAnswer)}</div>");
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

            // Check file access before proceeding (skip if already checked)
            if (!skipFileAccessCheck && !CheckOutputFileAccess(outputFile))
                return;

            await File.WriteAllTextAsync(outputFile, sb.ToString());
            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]HTML results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Output results as PDF file using iText7 (non-progress version for default file output)
        /// </summary>
        private async Task OutputPdfAsync(QcScoringResults results, QcResultsFile resultsFile)
        {
            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".pdf");
            }

            // Check file access before proceeding
            if (!CheckOutputFileAccess(outputFile))
                return;

            await Task.Run(() => GeneratePdfWithIText7(results, resultsFile, outputFile));

            var fileInfo = new FileInfo(outputFile);
            AnsiConsole.MarkupLine($"[green]PDF results saved to: {outputFile} ({ByteSize.FromBytes(fileInfo.Length)})[/]");
        }

        /// <summary>
        /// Core PDF generation using iText7 (called by both sync and progress methods)
        /// </summary>
        private void GeneratePdfWithIText7(QcScoringResults results, QcResultsFile resultsFile, string outputFile, Action<string, int>? progressCallback = null)
        {
            // Fonts
            var normalFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var boldFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var codeFont = PdfFontFactory.CreateFont(StandardFonts.COURIER); // Monospace for code/answers

            // Colors
            var headerBg = new DeviceRgb(30, 58, 138); // Blue
            var headerText = ColorConstants.WHITE;
            var altRowBg = new DeviceRgb(243, 244, 246); // Light gray
            var greenColor = new DeviceRgb(21, 128, 61);
            var orangeColor = new DeviceRgb(194, 65, 12);
            var redColor = new DeviceRgb(185, 28, 28);
            var blueTitle = new DeviceRgb(30, 64, 175);
            var grayText = new DeviceRgb(107, 114, 128);
            var lightBlue = new DeviceRgb(239, 246, 255);

            // Helper functions
            DeviceRgb GetScoreColor(double score) => score >= 80 ? greenColor : score >= 60 ? orangeColor : redColor;
            DeviceRgb GetSpeedColor(double pct) => pct >= 100 ? greenColor : pct >= 95 ? orangeColor : redColor;
            string SafeText(string? text) => string.IsNullOrEmpty(text) ? "N/A" : text;

            var quantScoresForPdf = results.QuantScores.OrderByDescending(q => q.FinalScore).ToList();
            var baseResult = resultsFile.Results.FirstOrDefault(r => r.IsBase);

            using var writer = new PdfWriter(outputFile);
            using var pdf = new PdfDocument(writer);
            using var document = new iTextDocument(pdf, PageSize.A4.Rotate());
            document.SetMargins(23, 23, 23, 23); // ~0.8cm

            progressCallback?.Invoke("Building header...", 5);

            // === PAGE 1: Main Results ===
            // Compact header - Title and info on same line area
            document.Add(new iTextParagraph("Quantization Comparison Results")
                .SetFontSize(14).SetFont(boldFont).SetFontColor(blueTitle).SetMarginBottom(4));

            // Compact header info - single line format
            var headerParts = new List<string>();
            headerParts.Add($"Model: {results.BaseModelName}");
            headerParts.Add($"Family: {results.BaseFamily}");
            headerParts.Add($"Size: {results.BaseParameterSize}");
            headerParts.Add($"Test: {results.TestSuiteName} ({results.TotalQuestions}q)");
            if (results.HasJudgmentScoring && !string.IsNullOrEmpty(results.JudgeModel))
            {
                var judgeDisplay = FormatCloudProviderPdf(results.JudgeProvider, results.JudgeApiVersion) is { } badge
                    ? $"{results.JudgeModel} {badge}" : results.JudgeModel;
                headerParts.Add($"Judge: {judgeDisplay}");

                // Add BestAnswer judge if different from main judge
                if (!string.IsNullOrEmpty(results.JudgeModelBestAnswer) && results.JudgeModelBestAnswer != results.JudgeModel)
                {
                    var bestJudgeDisplay = FormatCloudProviderPdf(results.JudgeBestProvider, results.JudgeBestApiVersion) is { } bestBadge
                        ? $"{results.JudgeModelBestAnswer} {bestBadge}" : results.JudgeModelBestAnswer;
                    headerParts.Add($"Best Judge: {bestJudgeDisplay}");
                }
            }
            document.Add(new iTextParagraph(string.Join(" | ", headerParts))
                .SetFontSize(8).SetFontColor(grayText));

            // Version info (compact)
            var versionParts = new List<string>();
            if (!string.IsNullOrEmpty(results.OsyncVersion)) versionParts.Add($"osync {results.OsyncVersion}");
            if (!string.IsNullOrEmpty(results.OllamaVersion)) versionParts.Add($"Ollama {results.OllamaVersion}");
            if (versionParts.Count > 0)
            {
                document.Add(new iTextParagraph($"Versions: {string.Join(" | ", versionParts)}")
                    .SetFontSize(7).SetFontColor(grayText).SetMarginTop(2));
            }

            // Base info (compact)
            document.Add(new iTextParagraph($"Base: {results.BaseTag} ({results.BaseQuantizationType}) | {ByteSize.FromBytes(results.BaseDiskSizeBytes)} | Eval: {results.BaseEvalTokensPerSecond:F1} tok/s | Prompt: {results.BasePromptTokensPerSecond:F1} tok/s")
                .SetFontSize(7).SetMarginTop(4));

            progressCallback?.Invoke("Building main table...", 15);

            // Main Results Table
            document.Add(new iTextParagraph("Quantization Quality & Performance")
                .SetFontSize(10).SetFont(boldFont).SetMarginTop(8));

            var numCols = results.HasJudgmentScoring ? 13 : 10;
            var mainTable = new iTextTable(numCols).UseAllAvailableWidth().SetFontSize(7).SetMarginTop(4);

            // Header row
            void AddHeader(string text) => mainTable.AddHeaderCell(new iTextCell()
                .Add(new iTextParagraph(text).SetFont(boldFont).SetFontColor(headerText))
                .SetBackgroundColor(headerBg).SetPadding(3));

            AddHeader("Tag"); AddHeader("Quant"); AddHeader("Size");
            if (results.HasJudgmentScoring) { AddHeader("Final"); AddHeader("Metrics"); AddHeader("Judge"); AddHeader("Best Ans"); }
            else { AddHeader("Score"); }
            AddHeader("Token"); AddHeader("Logprobs"); AddHeader("Length"); AddHeader("Perplexity"); AddHeader("Eval"); AddHeader("Prompt");

            // Data rows
            for (int i = 0; i < quantScoresForPdf.Count; i++)
            {
                var quant = quantScoresForPdf[i];
                var bg = i % 2 == 0 ? ColorConstants.WHITE : altRowBg;
                var quantDisplay = quant.EnhancedQuantization ?? quant.QuantizationType;
                var tokenScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.TokenSimilarityScore) : 0;
                var logprobsScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LogprobsDivergenceScore) : 0;
                var lengthScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.LengthConsistencyScore) : 0;
                var perplexityScore = quant.QuestionScores?.Count > 0 ? quant.QuestionScores.Average(q => q.PerplexityScore) : 0;

                void AddDataCell(string text, iTextColor? color = null) => mainTable.AddCell(new iTextCell()
                    .Add(new iTextParagraph(text).SetFontColor(color ?? ColorConstants.BLACK))
                    .SetBackgroundColor(bg).SetPadding(2));

                AddDataCell(quant.Tag); AddDataCell(quantDisplay);
                AddDataCell(ByteSize.FromBytes(quant.DiskSizeBytes).ToString());

                if (results.HasJudgmentScoring)
                {
                    AddDataCell($"{quant.FinalScore:F1}%", GetScoreColor(quant.FinalScore));
                    AddDataCell($"{quant.TotalConfidenceScore:F1}%", GetScoreColor(quant.TotalConfidenceScore));
                    var js = quant.AverageJudgmentScore ?? 0;
                    AddDataCell($"{quant.AverageJudgmentScore?.ToString("F1") ?? "N/A"}%", GetScoreColor(js));
                    AddDataCell(FormatBestStatsPlain(quant));
                }
                else { AddDataCell($"{quant.TotalConfidenceScore:F1}%", GetScoreColor(quant.TotalConfidenceScore)); }

                AddDataCell($"{tokenScore:F1}%", GetScoreColor(tokenScore));
                AddDataCell($"{logprobsScore:F1}%", GetScoreColor(logprobsScore));
                AddDataCell($"{lengthScore:F1}%", GetScoreColor(lengthScore));
                AddDataCell($"{perplexityScore:F1}%", GetScoreColor(perplexityScore));
                AddDataCell($"{quant.EvalPerformancePercent:F0}%", GetSpeedColor(quant.EvalPerformancePercent));
                AddDataCell($"{quant.PromptPerformancePercent:F0}%", GetSpeedColor(quant.PromptPerformancePercent));
            }
            document.Add(mainTable);

            progressCallback?.Invoke("Building category scores...", 25);

            // Category Scores (if available)
            if (quantScoresForPdf.Any() && quantScoresForPdf.First().CategoryScores.Any())
            {
                var categories = quantScoresForPdf.First().CategoryScores.Keys.ToList();
                document.Add(new iTextParagraph("Scores by Category").SetFontSize(11).SetFont(boldFont).SetMarginTop(12));

                var catColCount = 1 + categories.Count * (results.HasJudgmentScoring ? 2 : 1);
                var catTable = new iTextTable(catColCount).UseAllAvailableWidth().SetFontSize(7).SetMarginTop(4);

                catTable.AddHeaderCell(new iTextCell().Add(new iTextParagraph("Tag").SetFont(boldFont).SetFontColor(headerText)).SetBackgroundColor(headerBg).SetPadding(2));
                foreach (var cat in categories)
                {
                    catTable.AddHeaderCell(new iTextCell().Add(new iTextParagraph($"{cat}\nMetrics").SetFont(boldFont).SetFontColor(headerText).SetFontSize(6)).SetBackgroundColor(headerBg).SetPadding(2));
                    if (results.HasJudgmentScoring)
                        catTable.AddHeaderCell(new iTextCell().Add(new iTextParagraph($"{cat}\nJudge").SetFont(boldFont).SetFontColor(headerText).SetFontSize(6)).SetBackgroundColor(headerBg).SetPadding(2));
                }

                for (int i = 0; i < quantScoresForPdf.Count; i++)
                {
                    var quant = quantScoresForPdf[i];
                    var bg = i % 2 == 0 ? ColorConstants.WHITE : altRowBg;
                    catTable.AddCell(new iTextCell().Add(new iTextParagraph(quant.Tag)).SetBackgroundColor(bg).SetPadding(2));
                    foreach (var cat in categories)
                    {
                        var metricScore = quant.CategoryScores.GetValueOrDefault(cat);
                        catTable.AddCell(new iTextCell().Add(new iTextParagraph($"{metricScore:F1}%").SetFontColor(GetScoreColor(metricScore))).SetBackgroundColor(bg).SetPadding(2));
                        if (results.HasJudgmentScoring)
                        {
                            var judgeScore = quant.CategoryJudgmentScores.GetValueOrDefault(cat);
                            catTable.AddCell(new iTextCell().Add(new iTextParagraph($"{judgeScore:F1}%").SetFontColor(GetScoreColor(judgeScore))).SetBackgroundColor(bg).SetPadding(2));
                        }
                    }
                }
                document.Add(catTable);
            }

            // Footer for first page
            document.Add(new iTextParagraph($"Generated by osync qcview on {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                .SetFontSize(8).SetTextAlignment(TextAlignment.CENTER).SetMarginTop(15));

            progressCallback?.Invoke("Building rankings...", 35);

            // === PAGE 2: Rankings ===
            document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));
            document.Add(new iTextParagraph("Rankings").SetFontSize(14).SetFont(boldFont).SetFontColor(blueTitle));

            // Rankings tables - wrapped in Div with KeepTogether to prevent page breaks
            void AddRankingTable(string title, List<(string tag, string quant, string value, DeviceRgb color)> data)
            {
                var container = new Div().SetKeepTogether(true);
                container.Add(new iTextParagraph(title).SetFontSize(10).SetFont(boldFont).SetMarginTop(10));
                var rt = new iTextTable(4).UseAllAvailableWidth().SetFontSize(7).SetMarginTop(2);
                rt.AddHeaderCell(new iTextCell().Add(new iTextParagraph("#").SetFont(boldFont).SetFontColor(headerText)).SetBackgroundColor(headerBg).SetPadding(2));
                rt.AddHeaderCell(new iTextCell().Add(new iTextParagraph("Tag").SetFont(boldFont).SetFontColor(headerText)).SetBackgroundColor(headerBg).SetPadding(2));
                rt.AddHeaderCell(new iTextCell().Add(new iTextParagraph("Quant").SetFont(boldFont).SetFontColor(headerText)).SetBackgroundColor(headerBg).SetPadding(2));
                rt.AddHeaderCell(new iTextCell().Add(new iTextParagraph("Score").SetFont(boldFont).SetFontColor(headerText)).SetBackgroundColor(headerBg).SetPadding(2));
                for (int i = 0; i < data.Count; i++)
                {
                    var (tag, quant, value, color) = data[i];
                    var bg = i % 2 == 0 ? ColorConstants.WHITE : altRowBg;
                    rt.AddCell(new iTextCell().Add(new iTextParagraph($"{i + 1}")).SetBackgroundColor(bg).SetPadding(2));
                    rt.AddCell(new iTextCell().Add(new iTextParagraph(tag)).SetBackgroundColor(bg).SetPadding(2));
                    rt.AddCell(new iTextCell().Add(new iTextParagraph(quant)).SetBackgroundColor(bg).SetPadding(2));
                    rt.AddCell(new iTextCell().Add(new iTextParagraph(value).SetFontColor(color)).SetBackgroundColor(bg).SetPadding(2));
                }
                container.Add(rt);
                document.Add(container);
            }

            AddRankingTable($"By {(results.HasJudgmentScoring ? "Final Score" : "Metrics Score")}",
                quantScoresForPdf.Select(q => (q.Tag, q.EnhancedQuantization ?? q.QuantizationType, $"{q.FinalScore:F1}%", GetScoreColor(q.FinalScore))).ToList());

            AddRankingTable("By Eval Speed",
                quantScoresForPdf.OrderByDescending(q => q.EvalTokensPerSecond)
                    .Select(q => (q.Tag, q.EnhancedQuantization ?? q.QuantizationType, $"{q.EvalTokensPerSecond:F1} tok/s", GetSpeedColor(q.EvalPerformancePercent))).ToList());

            AddRankingTable("By Prompt Speed",
                quantScoresForPdf.OrderByDescending(q => q.PromptTokensPerSecond)
                    .Select(q => (q.Tag, q.EnhancedQuantization ?? q.QuantizationType, $"{q.PromptTokensPerSecond:F1} tok/s", GetSpeedColor(q.PromptPerformancePercent))).ToList());

            AddRankingTable("By Perplexity Score",
                quantScoresForPdf.OrderByDescending(q => q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0)
                    .Select(q => {
                        var ps = q.QuestionScores?.Count > 0 ? q.QuestionScores.Average(qs => qs.PerplexityScore) : 0;
                        return (q.Tag, q.EnhancedQuantization ?? q.QuantizationType, $"{ps:F1}%", GetScoreColor(ps));
                    }).ToList());

            if (results.HasJudgmentScoring)
            {
                AddRankingTable("By Best Answers",
                    quantScoresForPdf.OrderByDescending(q => { var t = q.BestCount + q.WorstCount + q.TieCount; return t > 0 ? q.BestCount * 100.0 / t : 0; })
                        .Select(q => {
                            var t = q.BestCount + q.WorstCount + q.TieCount;
                            var wp = t > 0 ? q.BestCount * 100.0 / t : 0;
                            return (q.Tag, q.EnhancedQuantization ?? q.QuantizationType, $"{wp:F0}%", GetScoreColor(wp));
                        }).ToList());
            }

            progressCallback?.Invoke("Building Q&A pages...", 45);

            // === Q&A Pages (Portrait orientation) ===
            var resultsForPdf = resultsFile.Results.Where(r => !r.IsBase)
                .OrderByDescending(r => quantScoresForPdf.FirstOrDefault(q => q.Tag == r.Tag)?.FinalScore ?? 0).ToList();

            int qaProgress = 0;
            int qaTotal = resultsForPdf.Count;

            // Set default page size to portrait BEFORE creating Q&A pages
            pdf.SetDefaultPageSize(PageSize.A4);

            foreach (var quantResult in resultsForPdf)
            {
                var quantScore = quantScoresForPdf.FirstOrDefault(q => q.Tag == quantResult.Tag);

                // Add page break - new pages will use the portrait default size
                document.Add(new AreaBreak(AreaBreakType.NEXT_PAGE));

                document.Add(new iTextParagraph($"Q&A: {quantResult.Tag}").SetFontSize(12).SetFont(boldFont).SetFontColor(blueTitle));
                if (quantScore != null)
                    document.Add(new iTextParagraph($"Score: {quantScore.FinalScore:F1}% | {quantScore.EnhancedQuantization ?? quantScore.QuantizationType}").SetFontSize(9));

                foreach (var question in quantResult.QuestionResults)
                {
                    var baseQuestion = baseResult?.QuestionResults.FirstOrDefault(q => q.QuestionId == question.QuestionId);

                    // Question header with separator
                    document.Add(new iTextParagraph($"Q{question.QuestionId}: {SafeText(question.Question)}")
                        .SetFont(boldFont).SetFontSize(8).SetMarginTop(10).SetBorderTop(new iText.Layout.Borders.SolidBorder(grayText, 0.5f)).SetPaddingTop(4));

                    if (question.Judgment != null)
                    {
                        var bestLabel = question.Judgment.BestAnswer == "B" ? "Quant Better" :
                                        question.Judgment.BestAnswer == "A" ? "Base Better" : "Tied";
                        var jColor = question.Judgment.BestAnswer == "B" ? greenColor :
                                     question.Judgment.BestAnswer == "A" ? redColor : orangeColor;
                        document.Add(new iTextParagraph($"[{bestLabel}] Similarity: {question.Judgment.Score}%")
                            .SetFontSize(7).SetFontColor(jColor).SetMarginTop(2));

                        // Add Similarity reason if available
                        if (!string.IsNullOrEmpty(question.Judgment.Reason))
                        {
                            document.Add(new iTextParagraph($"Similarity Reason: {question.Judgment.Reason}")
                                .SetFontSize(6).SetFixedLeading(8).SetFontColor(grayText).SetMarginTop(1));
                        }

                        // Add BestAnswer reason if available
                        if (!string.IsNullOrEmpty(question.Judgment.ReasonBestAnswer))
                        {
                            document.Add(new iTextParagraph($"Best Answer Reason: {question.Judgment.ReasonBestAnswer}")
                                .SetFontSize(6).SetFixedLeading(8).SetFontColor(grayText).SetMarginTop(1));
                        }
                    }

                    // Base answer with monospace font for code content
                    document.Add(new iTextParagraph("Base (A):").SetFont(boldFont).SetFontSize(7).SetMarginTop(4));
                    document.Add(CreateCodeParagraph(SanitizeForPdf(baseQuestion?.Answer), codeFont, 5, 7, altRowBg));

                    // Quant answer with monospace font for code content
                    document.Add(new iTextParagraph("Quant (B):").SetFont(boldFont).SetFontSize(7).SetMarginTop(3));
                    document.Add(CreateCodeParagraph(SanitizeForPdf(question.Answer), codeFont, 5, 7, lightBlue));
                }

                qaProgress++;
                progressCallback?.Invoke($"Building Q&A pages ({qaProgress}/{qaTotal})...", 45 + (qaProgress * 45 / qaTotal));
            }

            progressCallback?.Invoke("Finalizing...", 95);
        }

        /// <summary>
        /// Create a simple info cell for the header table
        /// </summary>
        private static iTextCell CreateInfoCell(string text, PdfFont? boldFont = null)
        {
            var para = new iTextParagraph(text);
            if (boldFont != null) para.SetFont(boldFont);
            return new iTextCell().Add(para).SetBorder(iText.Layout.Borders.Border.NO_BORDER);
        }

        /// <summary>
        /// Output results as PDF file with progress tracking
        /// </summary>
        private async Task OutputPdfWithProgressAsync(QcScoringResults results, QcResultsFile resultsFile, ProgressTask progressTask)
        {
            var outputFile = _args.OutputFile;
            if (string.IsNullOrWhiteSpace(outputFile))
            {
                outputFile = Path.ChangeExtension(_args.FileName, ".pdf");
            }

            // Note: File access check is done BEFORE progress bar starts in ExecuteAsync
            // to avoid concurrent interactive display errors

            progressTask.Description = "[cyan]Preparing PDF document...[/]";
            progressTask.Increment(5);

            await Task.Run(() =>
            {
                GeneratePdfWithIText7(results, resultsFile, outputFile, (description, progress) =>
                {
                    progressTask.Description = $"[cyan]{description}[/]";
                    // Map 0-100 progress to 5-95 range (we already did 5 at start, 95-100 is for finalization)
                    progressTask.Value = 5 + (progress * 90 / 100);
                });
            });

            progressTask.Description = "[cyan]Finalizing...[/]";
            progressTask.Value = 95;

            var fileInfo = new FileInfo(outputFile);
            progressTask.Description = $"[green]PDF saved: {ByteSize.FromBytes(fileInfo.Length)}[/]";
            progressTask.Value = 100;
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

        /// <summary>
        /// Format cloud provider display for HTML output.
        /// Returns null if provider is ollama or not set (backward compatibility).
        /// </summary>
        private static string? FormatCloudProviderHtml(string? provider, string? apiVersion)
        {
            if (string.IsNullOrEmpty(provider) || provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return null;

            var versionPart = string.IsNullOrEmpty(apiVersion) ? "" : $" {EscapeHtml(apiVersion)}";
            return $"<span class=\"cloud-provider-badge\">[Cloud: {EscapeHtml(provider)}{versionPart}]</span>";
        }

        /// <summary>
        /// Format cloud provider display for console/Spectre output.
        /// Returns null if provider is ollama or not set (backward compatibility).
        /// </summary>
        private static string? FormatCloudProviderConsole(string? provider, string? apiVersion)
        {
            if (string.IsNullOrEmpty(provider) || provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return null;

            var versionPart = string.IsNullOrEmpty(apiVersion) ? "" : $" {apiVersion}";
            return $"[dim][[Cloud: {Markup.Escape(provider)}{Markup.Escape(versionPart)}]][/]";
        }

        /// <summary>
        /// Format cloud provider display for markdown output.
        /// Returns null if provider is ollama or not set (backward compatibility).
        /// </summary>
        private static string? FormatCloudProviderMarkdown(string? provider, string? apiVersion)
        {
            if (string.IsNullOrEmpty(provider) || provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return null;

            var versionPart = string.IsNullOrEmpty(apiVersion) ? "" : $" {apiVersion}";
            return $"*[Cloud: {provider}{versionPart}]*";
        }

        /// <summary>
        /// Sanitize text for PDF rendering.
        /// Replaces characters that standard PDF fonts can't handle with safe equivalents.
        /// Also escapes % characters which can cause rendering issues in iText7.
        /// </summary>
        private static string SanitizeForPdf(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "N/A";

            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                // Keep standard ASCII printable characters (32-126), newlines, and tabs
                if ((c >= 32 && c <= 126) || c == '\n' || c == '\r' || c == '\t')
                {
                    sb.Append(c);
                }
                else if (c >= 128)
                {
                    // Replace non-ASCII with closest ASCII equivalent or placeholder
                    var replacement = c switch
                    {
                        '\u2018' or '\u2019' => '\'', // Smart quotes
                        '\u201C' or '\u201D' => '"',  // Smart double quotes
                        '\u2013' or '\u2014' => '-',  // En/em dash
                        '\u2026' => '.',              // Ellipsis
                        '\u00A0' => ' ',              // Non-breaking space
                        '\u00B0' => 'o',              // Degree symbol
                        '\u00D7' => 'x',              // Multiplication sign
                        '\u2022' => '*',              // Bullet
                        '\u2192' => '>',              // Right arrow
                        '\u2190' => '<',              // Left arrow
                        '\u2264' => '<',              // Less than or equal
                        '\u2265' => '>',              // Greater than or equal
                        '\u2260' => '!',              // Not equal
                        '\u221E' => '~',              // Infinity
                        _ => '?'                      // Unknown character
                    };
                    sb.Append(replacement);
                }
                // Skip control characters (0-31 except newline/tab)
            }
            return sb.ToString();
        }

        /// <summary>
        /// Create a paragraph for code/answer content with proper text handling.
        /// Uses explicit Text elements and disables problematic text features.
        /// </summary>
        private static iTextParagraph CreateCodeParagraph(string text, PdfFont font, float fontSize, float leading, iTextColor bgColor)
        {
            var para = new iTextParagraph()
                .SetFont(font)
                .SetFontSize(fontSize)
                .SetFixedLeading(leading)
                .SetBackgroundColor(bgColor)
                .SetPadding(4)
                .SetMarginBottom(2);

            // Add text line by line to avoid text processing issues
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                {
                    para.Add("\n");
                }
                // Add each line as a separate Text element to prevent reordering
                para.Add(new iText.Layout.Element.Text(lines[i]));
            }

            return para;
        }

        /// <summary>
        /// Format cloud provider display for PDF output.
        /// Returns null if provider is ollama or not set (backward compatibility).
        /// </summary>
        private static string? FormatCloudProviderPdf(string? provider, string? apiVersion)
        {
            if (string.IsNullOrEmpty(provider) || provider.Equals("ollama", StringComparison.OrdinalIgnoreCase))
                return null;

            var versionPart = string.IsNullOrEmpty(apiVersion) ? "" : $" {apiVersion}";
            return $"[Cloud: {provider}{versionPart}]";
        }

        /// <summary>
        /// Check if output file can be written, handling overwrite confirmation and file lock retry.
        /// Returns true if file can be written, false if user cancelled.
        /// </summary>
        private static bool CheckOutputFileAccess(string filePath)
        {
            // Check if file exists and prompt for overwrite
            if (File.Exists(filePath))
            {
                if (!AnsiConsole.Confirm($"[yellow]Output file already exists:[/] {filePath}\n[yellow]Overwrite?[/]", defaultValue: false))
                {
                    AnsiConsole.MarkupLine("[dim]Operation cancelled.[/]");
                    return false;
                }
            }

            // Try to ensure the file can be written (check for locks)
            return TryAccessFile(filePath);
        }

        /// <summary>
        /// Try to access a file for writing, with retry option if locked.
        /// </summary>
        private static bool TryAccessFile(string filePath)
        {
            while (true)
            {
                try
                {
                    // Try to open the file for writing to check if it's locked
                    if (File.Exists(filePath))
                    {
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
                        // File is accessible, close it
                    }
                    return true;
                }
                catch (IOException ex) when (IsFileLocked(ex))
                {
                    AnsiConsole.MarkupLine($"[red]Error: File is locked by another process:[/] {filePath}");
                    if (!AnsiConsole.Confirm("[yellow]Retry?[/]", defaultValue: true))
                    {
                        AnsiConsole.MarkupLine("[dim]Operation cancelled.[/]");
                        return false;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Access denied to file:[/] {filePath}");
                    if (!AnsiConsole.Confirm("[yellow]Retry?[/]", defaultValue: true))
                    {
                        AnsiConsole.MarkupLine("[dim]Operation cancelled.[/]");
                        return false;
                    }
                }
                catch
                {
                    // File doesn't exist or other error - can proceed
                    return true;
                }
            }
        }

        /// <summary>
        /// Check if an IOException is due to file being locked
        /// </summary>
        private static bool IsFileLocked(IOException ex)
        {
            // Common HRESULT values for file lock errors
            const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
            const int ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);

            var hResult = ex.HResult;
            return hResult == ERROR_SHARING_VIOLATION || hResult == ERROR_LOCK_VIOLATION;
        }
    }
}
