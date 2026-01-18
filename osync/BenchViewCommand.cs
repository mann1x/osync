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
using SpectreTable = Spectre.Console.Table;
using Path = System.IO.Path;

namespace osync
{
    /// <summary>
    /// Implementation of the benchview command.
    /// Displays benchmark results in table, JSON, Markdown, HTML, or PDF format.
    /// </summary>
    public class BenchViewCommand
    {
        private readonly BenchViewArgs _args;
        private string[] _inputFileNames = Array.Empty<string>();

        public BenchViewCommand(BenchViewArgs args)
        {
            _args = args;
        }

        /// <summary>
        /// Main execution method
        /// </summary>
        public async Task<int> ExecuteAsync()
        {
            try
            {
                // Validate filename
                if (string.IsNullOrWhiteSpace(_args.FileName))
                {
                    AnsiConsole.MarkupLine("[red]Error: Results file name is required[/]");
                    AnsiConsole.MarkupLine("Usage: osync benchview <filename>[,filename2,...]");
                    return 1;
                }

                // Parse comma-separated file names
                _inputFileNames = _args.FileName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Load all result files
                var resultsFiles = new List<BenchResultsFile>();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                foreach (var fileName in _inputFileNames)
                {
                    if (!File.Exists(fileName))
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Results file not found: {fileName}[/]");
                        return 1;
                    }

                    await using var fileStream = File.OpenRead(fileName);
                    var resultsFile = await JsonSerializer.DeserializeAsync<BenchResultsFile>(fileStream, options);

                    if (resultsFile == null)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Failed to parse results file: {fileName}[/]");
                        return 1;
                    }

                    if (resultsFile.Results.Count == 0)
                    {
                        AnsiConsole.MarkupLine($"[yellow]No results found in file: {fileName}[/]");
                        continue;
                    }

                    resultsFiles.Add(resultsFile);
                }

                if (resultsFiles.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No results found in any file[/]");
                    return 0;
                }

                // Validate compatibility if multiple files
                if (resultsFiles.Count > 1)
                {
                    var validationError = ValidateFilesCompatibility(resultsFiles);
                    if (validationError != null)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {validationError}[/]");
                        return 1;
                    }
                }

                // Merge results into a single file for processing
                var mergedResultsFile = MergeResultsFiles(resultsFiles);

                // Apply category filter if specified
                if (!string.IsNullOrWhiteSpace(_args.Category))
                {
                    FilterByCategory(mergedResultsFile, _args.Category);
                }

                // Calculate scoring results
                var scoringResults = BenchScoring.CalculateScoringResults(mergedResultsFile);

                // Determine output format
                var format = string.IsNullOrWhiteSpace(_args.Format) ? "table" : _args.Format.ToLower();
                var isFileOutput = !string.IsNullOrEmpty(_args.OutputFile) && format != "table";

                // Check file access before progress bar
                if (isFileOutput)
                {
                    var outputFile = GetOutputFilePath(format);
                    if (!CheckOutputFileAccess(outputFile))
                        return 0;
                }

                if (isFileOutput)
                {
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
                            var task = ctx.AddTask("[cyan]Generating report[/]", maxValue: 100);
                            task.Increment(20);

                            switch (format)
                            {
                                case "json":
                                    task.Description = "[cyan]Writing JSON...[/]";
                                    await OutputJsonAsync(scoringResults, mergedResultsFile, true);
                                    break;
                                case "md":
                                case "markdown":
                                    task.Description = "[cyan]Generating Markdown...[/]";
                                    await OutputMarkdownAsync(scoringResults, mergedResultsFile, true);
                                    break;
                                case "html":
                                    task.Description = "[cyan]Generating HTML...[/]";
                                    await OutputHtmlAsync(scoringResults, mergedResultsFile, true);
                                    break;
                                case "pdf":
                                    task.Description = "[cyan]Generating PDF...[/]";
                                    await OutputPdfAsync(scoringResults, mergedResultsFile, task);
                                    break;
                            }

                            task.Value = 100;
                            task.Description = "[green]Complete[/]";
                        });
                }
                else
                {
                    switch (format)
                    {
                        case "json":
                            await OutputJsonAsync(scoringResults, mergedResultsFile, false);
                            break;
                        case "md":
                        case "markdown":
                            await OutputMarkdownAsync(scoringResults, mergedResultsFile, false);
                            break;
                        case "html":
                            await OutputHtmlAsync(scoringResults, mergedResultsFile, false);
                            break;
                        case "pdf":
                            await OutputPdfAsync(scoringResults, mergedResultsFile, null);
                            break;
                        default:
                            OutputTable(scoringResults, mergedResultsFile);
                            break;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }
        }

        private void FilterByCategory(BenchResultsFile resultsFile, string category)
        {
            foreach (var result in resultsFile.Results)
            {
                if (result.CategoryResults != null)
                {
                    result.CategoryResults = result.CategoryResults
                        .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                }
            }
        }

        /// <summary>
        /// Get subcategories ordered by test suite order (from first quant's results).
        /// </summary>
        private List<(string Category, string SubCategory, string Key)> GetOrderedSubCategories(BenchResultsFile resultsFile)
        {
            var orderedSubCategories = new List<(string Category, string SubCategory, string Key)>();
            var firstQuant = resultsFile.Results.FirstOrDefault();
            if (firstQuant?.CategoryResults != null)
            {
                foreach (var cat in firstQuant.CategoryResults)
                {
                    if (cat.SubCategoryResults != null)
                    {
                        foreach (var sub in cat.SubCategoryResults)
                        {
                            var key = $"{cat.Category}/{sub.SubCategory}";
                            orderedSubCategories.Add((cat.Category, sub.SubCategory, key));
                        }
                    }
                }
            }
            return orderedSubCategories;
        }

        /// <summary>
        /// Validate that multiple results files are compatible for comparison.
        /// </summary>
        private string? ValidateFilesCompatibility(List<BenchResultsFile> files)
        {
            var first = files[0];

            for (int i = 1; i < files.Count; i++)
            {
                var current = files[i];

                // Check test type
                if (!string.Equals(first.TestType, current.TestType, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Test type mismatch: '{first.TestType}' vs '{current.TestType}'";
                }

                // Check test suite digest (ensures identical test suite was used)
                var firstDigest = first.TestSuiteDigest ?? "";
                var currentDigest = current.TestSuiteDigest ?? "";
                if (!string.IsNullOrEmpty(firstDigest) && !string.IsNullOrEmpty(currentDigest) &&
                    !string.Equals(firstDigest, currentDigest, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Test suite mismatch: Results were created with different test suite versions.\n" +
                           $"  File 1 digest: {firstDigest}\n" +
                           $"  File {i + 1} digest: {currentDigest}";
                }

                // Check judge model
                var firstJudge = first.JudgeModel ?? "";
                var currentJudge = current.JudgeModel ?? "";
                if (!string.Equals(firstJudge, currentJudge, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Judge model mismatch: '{firstJudge}' vs '{currentJudge}'";
                }

                // Check judge provider
                var firstProvider = first.JudgeProvider ?? "ollama";
                var currentProvider = current.JudgeProvider ?? "ollama";
                if (!string.Equals(firstProvider, currentProvider, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Judge provider mismatch: '{firstProvider}' vs '{currentProvider}'";
                }

                // Check test options (seed, temperature, thinking)
                if (first.Options.Seed != current.Options.Seed)
                {
                    return $"Test seed mismatch: {first.Options.Seed} vs {current.Options.Seed}";
                }

                if (first.Options.EnableThinking != current.Options.EnableThinking)
                {
                    return $"Thinking mode mismatch: {first.Options.EnableThinking} vs {current.Options.EnableThinking}";
                }

                var firstThinkLevel = first.Options.ThinkLevel ?? "";
                var currentThinkLevel = current.Options.ThinkLevel ?? "";
                if (!string.Equals(firstThinkLevel, currentThinkLevel, StringComparison.OrdinalIgnoreCase))
                {
                    return $"Think level mismatch: '{firstThinkLevel}' vs '{currentThinkLevel}'";
                }
            }

            return null; // All files are compatible
        }

        /// <summary>
        /// Merge multiple results files into a single file for processing.
        /// </summary>
        private BenchResultsFile MergeResultsFiles(List<BenchResultsFile> files)
        {
            if (files.Count == 1)
                return files[0];

            var first = files[0];

            // Create merged file using first file's metadata (since all are validated to be compatible)
            var merged = new BenchResultsFile
            {
                TestSuiteName = first.TestSuiteName,
                TestType = first.TestType,
                TestDescription = first.TestDescription,
                ModelName = "", // Don't set model name since we have multiple models
                JudgeModel = first.JudgeModel,
                JudgeProvider = first.JudgeProvider,
                JudgeApiVersion = first.JudgeApiVersion,
                Options = first.Options,
                TestedAt = first.TestedAt,
                OsyncVersion = first.OsyncVersion,
                OllamaVersion = first.OllamaVersion,
                MaxContextLength = first.MaxContextLength,
                CategoryLimit = first.CategoryLimit
            };

            // Merge all results from all files
            foreach (var file in files)
            {
                merged.Results.AddRange(file.Results);
            }

            return merged;
        }

        private string GetOutputFilePath(string format)
        {
            if (!string.IsNullOrWhiteSpace(_args.OutputFile))
                return _args.OutputFile;

            var ext = format switch
            {
                "json" => ".json",
                "md" or "markdown" => ".md",
                "html" => ".html",
                "pdf" => ".pdf",
                _ => ".txt"
            };

            // Build default filename from input filenames (strip extensions, join with dash)
            var baseNames = _inputFileNames
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToArray();
            var combinedName = string.Join("-", baseNames);
            return combinedName + ext;
        }

        private bool CheckOutputFileAccess(string filePath)
        {
            if (File.Exists(filePath) && !_args.Overwrite)
            {
                if (!AnsiConsole.Confirm($"[yellow]File '{filePath}' already exists. Overwrite?[/]"))
                {
                    AnsiConsole.MarkupLine("[dim]Operation cancelled[/]");
                    return false;
                }
            }
            return true;
        }

        #region Table Output

        private void OutputTable(BenchScoringResults scoring, BenchResultsFile resultsFile)
        {
            // Header (no model name shown since we may have multiple models)
            AnsiConsole.MarkupLine($"[bold]Benchmark Results: {scoring.TestType}[/]");
            AnsiConsole.MarkupLine($"[dim]{scoring.TestDescription}[/]");
            AnsiConsole.MarkupLine($"[dim]Test Suite: {scoring.TestSuiteName}[/]");

            if (!string.IsNullOrEmpty(scoring.JudgeModel))
            {
                var providerInfo = !string.IsNullOrEmpty(scoring.JudgeProvider) && scoring.JudgeProvider != "ollama"
                    ? $" [{scoring.JudgeProvider}]"
                    : "";
                AnsiConsole.MarkupLine($"[dim]Judge: {scoring.JudgeModel}{providerInfo}[/]");
            }

            // Show thinking settings if configured
            var thinkInfo = GetThinkingInfoText(scoring.Options);
            if (!string.IsNullOrEmpty(thinkInfo))
            {
                AnsiConsole.MarkupLine($"[dim]Thinking: {thinkInfo}[/]");
            }

            AnsiConsole.WriteLine();

            // Get categories for columns
            var categories = scoring.QuantScores.Count > 0
                ? scoring.QuantScores[0].CategoryScores.Keys.ToList()
                : new List<string>();

            // Main results table with scores
            AnsiConsole.MarkupLine("[bold underline]Scores by Category[/]");
            var table = new SpectreTable();
            table.AddColumn(new TableColumn("[bold]Model:Tag[/]").NoWrap());
            table.AddColumn(new TableColumn("[bold]Params[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Size[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Overall[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Correct[/]").RightAligned());

            // Add category columns
            foreach (var cat in categories)
            {
                table.AddColumn(new TableColumn($"[bold]{cat}[/]").RightAligned());
            }

            // Add min speed columns
            table.AddColumn(new TableColumn("[bold]Min Prompt[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Min Eval[/]").RightAligned());

            // Add rows
            foreach (var quant in scoring.QuantScores)
            {
                var scoreColor = BenchScoring.GetScoreColor(quant.OverallScore);
                var paramStr = string.IsNullOrEmpty(quant.ParameterSize) ? "-" : quant.ParameterSize;
                var sizeStr = quant.DiskSizeBytes > 0 ? BenchScoring.FormatSize(quant.DiskSizeBytes) : "-";
                var correctStr = $"{quant.CorrectAnswers}/{quant.TotalQuestions}";

                var row = new List<string>
                {
                    $"[bold]{Markup.Escape(quant.Tag)}[/]",
                    paramStr,
                    sizeStr,
                    $"[{scoreColor}]{quant.OverallScore:F1}%[/]",
                    correctStr
                };

                foreach (var cat in categories)
                {
                    var catScore = quant.CategoryScores.GetValueOrDefault(cat, 0);
                    var catColor = BenchScoring.GetScoreColor(catScore);
                    row.Add($"[{catColor}]{catScore:F1}%[/]");
                }

                // Calculate overall min speeds across all categories (format with k suffix)
                var minPrompt = quant.MinPromptToksPerSec.Values.Any() ? quant.MinPromptToksPerSec.Values.Min() : 0;
                var minEval = quant.MinEvalToksPerSec.Values.Any() ? quant.MinEvalToksPerSec.Values.Min() : 0;
                row.Add(minPrompt > 0 ? $"{BenchScoring.FormatSpeed(minPrompt)} t/s" : "-");
                row.Add(minEval > 0 ? $"{BenchScoring.FormatSpeed(minEval)} t/s" : "-");

                table.AddRow(row.ToArray());
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();

            // Subcategory scores table (if subcategories exist)
            OutputSubcategoryTable(scoring, resultsFile);

            // Average speed table
            OutputAvgSpeedTable(scoring, categories);

            // Show details if requested
            if (_args.Details)
            {
                OutputDetailedResults(resultsFile);
            }
        }

        /// <summary>
        /// Output subcategory scores table if subcategories exist.
        /// Uses two-row header with category spanning subcategories.
        /// </summary>
        private void OutputSubcategoryTable(BenchScoringResults scoring, BenchResultsFile resultsFile)
        {
            // Check if any quant has subcategory scores
            var hasSubcategories = scoring.QuantScores.Any(q => q.SubCategoryScores.Count > 0);
            if (!hasSubcategories)
                return;

            // Get subcategory order from first quant's category results (test suite order)
            var orderedSubCategories = new List<(string Category, string SubCategory, string Key)>();
            var firstQuant = resultsFile.Results.FirstOrDefault();
            if (firstQuant?.CategoryResults != null)
            {
                foreach (var cat in firstQuant.CategoryResults)
                {
                    if (cat.SubCategoryResults != null)
                    {
                        foreach (var sub in cat.SubCategoryResults)
                        {
                            var key = $"{cat.Category}/{sub.SubCategory}";
                            orderedSubCategories.Add((cat.Category, sub.SubCategory, key));
                        }
                    }
                }
            }

            if (orderedSubCategories.Count == 0)
                return;

            AnsiConsole.MarkupLine("[bold underline]Scores by Subcategory[/]");
            var table = new SpectreTable();
            table.Border(TableBorder.Rounded);

            // Build two-row header: category on first line, subcategory on second
            // First column is Model:Tag
            table.AddColumn(new TableColumn("[bold]Model:Tag[/]").NoWrap());

            string? lastCategory = null;
            foreach (var (category, subCategory, _) in orderedSubCategories)
            {
                // Show category name only for first subcategory of each category
                var categoryLine = category != lastCategory ? category : "";
                lastCategory = category;
                var header = $"[bold]{categoryLine}\n{subCategory}[/]";
                table.AddColumn(new TableColumn(header).RightAligned());
            }

            foreach (var quant in scoring.QuantScores)
            {
                var row = new List<string> { $"[bold]{Markup.Escape(quant.Tag)}[/]" };

                foreach (var (_, _, key) in orderedSubCategories)
                {
                    var score = quant.SubCategoryScores.GetValueOrDefault(key, 0);
                    var color = BenchScoring.GetScoreColor(score);
                    row.Add($"[{color}]{score:F1}%[/]");
                }

                table.AddRow(row.ToArray());
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        /// <summary>
        /// Output average speed table with response time per category.
        /// </summary>
        private void OutputAvgSpeedTable(BenchScoringResults scoring, List<string> categories)
        {
            AnsiConsole.MarkupLine("[bold underline]Average Speed per Category[/]");
            var table = new SpectreTable();
            table.AddColumn(new TableColumn("[bold]Model:Tag[/]").NoWrap());
            table.AddColumn(new TableColumn("[bold]Score[/]").RightAligned());

            // Add columns for each category's average response time
            foreach (var cat in categories)
            {
                table.AddColumn(new TableColumn($"[bold]{cat}[/]").RightAligned());
            }

            // Sort by overall score descending
            var sortedQuants = scoring.QuantScores
                .OrderByDescending(q => q.OverallScore)
                .ToList();

            foreach (var quant in sortedQuants)
            {
                var scoreColor = BenchScoring.GetScoreColor(quant.OverallScore);
                var row = new List<string>
                {
                    $"[bold]{Markup.Escape(quant.Tag)}[/]",
                    $"[{scoreColor}]{quant.OverallScore:F1}%[/]"
                };

                foreach (var cat in categories)
                {
                    var avgTime = quant.AvgResponseTimeMs.GetValueOrDefault(cat, 0);
                    row.Add(avgTime > 0 ? BenchScoring.FormatResponseTime(avgTime) : "-");
                }

                table.AddRow(row.ToArray());
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        private void OutputDetailedResults(BenchResultsFile resultsFile)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[bold]Detailed Q&A Results[/]");

            foreach (var quant in resultsFile.Results)
            {
                AnsiConsole.MarkupLine($"\n[bold underline]{quant.Tag}[/]");

                // Show per-tag metadata if available
                if (!string.IsNullOrEmpty(quant.OsyncVersion) || !string.IsNullOrEmpty(quant.JudgeModel))
                {
                    var metaParts = new List<string>();
                    if (!string.IsNullOrEmpty(quant.OsyncVersion))
                        metaParts.Add($"osync {quant.OsyncVersion}");
                    if (!string.IsNullOrEmpty(quant.OllamaVersion))
                        metaParts.Add($"Ollama {quant.OllamaVersion}");
                    if (!string.IsNullOrEmpty(quant.JudgeModel))
                    {
                        var judgeStr = quant.JudgeModel;
                        if (!string.IsNullOrEmpty(quant.JudgeProvider) && quant.JudgeProvider != "ollama")
                            judgeStr += $" ({quant.JudgeProvider})";
                        metaParts.Add($"Judge: {judgeStr}");
                    }
                    if (metaParts.Count > 0)
                        AnsiConsole.MarkupLine($"[dim]  {string.Join(" | ", metaParts)}[/]");
                }

                foreach (var cat in quant.CategoryResults ?? new List<BenchCategoryResult>())
                {
                    AnsiConsole.MarkupLine($"\n[bold]Category: {cat.Category}[/] ({cat.Score:F1}%)");

                    var questions = cat.QuestionResults ?? cat.SubCategoryResults?
                        .SelectMany(s => s.QuestionResults.Select(q => (s.SubCategory, q)))
                        .Select(x => x.q)
                        .ToList() ?? new List<BenchQuestionResult>();

                    foreach (var q in questions)
                    {
                        var status = q.Score >= 100 ? "[green]CORRECT[/]" : "[red]INCORRECT[/]";
                        AnsiConsole.MarkupLine($"  Q{q.QuestionId}: {status}");
                        AnsiConsole.MarkupLine($"    [dim]Q: {Markup.Escape(TruncateText(q.Question, 80))}[/]");
                        AnsiConsole.MarkupLine($"    [dim]Expected: {Markup.Escape(TruncateText(q.ReferenceAnswer, 60))}[/]");
                        AnsiConsole.MarkupLine($"    [dim]Got: {Markup.Escape(TruncateText(q.ModelAnswer, 60))}[/]");

                        if (q.Judgment != null)
                        {
                            AnsiConsole.MarkupLine($"    [dim]Judge: {q.Judgment.Answer} - {Markup.Escape(TruncateText(q.Judgment.Reason, 60))}[/]");
                        }

                        if (q.ToolsUsed.Count > 0)
                        {
                            var tools = string.Join(", ", q.ToolsUsed.Select(t => $"{t.ToolName}({t.CallCount})"));
                            AnsiConsole.MarkupLine($"    [dim]Tools: {tools}[/]");
                        }
                    }
                }
            }
        }

        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\n", " ").Replace("\r", "");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength - 3) + "...";
        }

        #endregion

        #region JSON Output

        private async Task OutputJsonAsync(BenchScoringResults scoring, BenchResultsFile resultsFile, bool skipAccessCheck)
        {
            var outputFile = GetOutputFilePath("json");

            if (!skipAccessCheck && !CheckOutputFileAccess(outputFile))
                return;

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            if (string.IsNullOrEmpty(_args.OutputFile))
            {
                // Console output
                var json = JsonSerializer.Serialize(scoring, options);
                Console.WriteLine(json);
            }
            else
            {
                var json = JsonSerializer.Serialize(_args.Details ? (object)resultsFile : scoring, options);
                await File.WriteAllTextAsync(outputFile, json);
                AnsiConsole.MarkupLine($"[green]JSON saved to: {outputFile}[/]");
            }
        }

        #endregion

        #region Markdown Output

        private async Task OutputMarkdownAsync(BenchScoringResults scoring, BenchResultsFile resultsFile, bool skipAccessCheck)
        {
            var sb = new StringBuilder();

            // Header (no model name since we may have multiple models)
            sb.AppendLine($"# Benchmark Results: {scoring.TestType}");
            sb.AppendLine();
            sb.AppendLine($"**Description:** {scoring.TestDescription}");
            sb.AppendLine($"**Test Suite:** {scoring.TestSuiteName}");
            sb.AppendLine($"**Tested:** {scoring.TestedAt:yyyy-MM-dd HH:mm:ss} UTC");

            if (!string.IsNullOrEmpty(scoring.JudgeModel))
            {
                var providerInfo = !string.IsNullOrEmpty(scoring.JudgeProvider) && scoring.JudgeProvider != "ollama"
                    ? $" ({scoring.JudgeProvider})"
                    : "";
                sb.AppendLine($"**Judge Model:** {scoring.JudgeModel}{providerInfo}");
            }

            // Show thinking settings if configured
            var thinkInfo = GetThinkingInfoText(scoring.Options);
            if (!string.IsNullOrEmpty(thinkInfo))
            {
                sb.AppendLine($"**Thinking:** {thinkInfo}");
            }

            sb.AppendLine();

            // Results table
            sb.AppendLine("## Scores by Category");
            sb.AppendLine();

            // Build header
            var categories = scoring.QuantScores.Count > 0
                ? scoring.QuantScores[0].CategoryScores.Keys.ToList()
                : new List<string>();

            sb.Append("| Model:Tag | Params | Size | Overall | Correct |");
            foreach (var cat in categories)
                sb.Append($" {cat} |");
            sb.Append(" Min Prompt | Min Eval |");
            sb.AppendLine();

            sb.Append("|-----------|--------|------|---------|---------|");
            foreach (var _ in categories)
                sb.Append("------|");
            sb.Append("------------|----------|");
            sb.AppendLine();

            // Data rows
            foreach (var quant in scoring.QuantScores)
            {
                var paramStr = string.IsNullOrEmpty(quant.ParameterSize) ? "-" : quant.ParameterSize;
                var size = quant.DiskSizeBytes > 0 ? BenchScoring.FormatSize(quant.DiskSizeBytes) : "-";
                sb.Append($"| {quant.Tag} | {paramStr} | {size} | {quant.OverallScore:F1}% | {quant.CorrectAnswers}/{quant.TotalQuestions} |");

                foreach (var cat in categories)
                {
                    var score = quant.CategoryScores.GetValueOrDefault(cat, 0);
                    sb.Append($" {score:F1}% |");
                }

                var minPrompt = quant.MinPromptToksPerSec.Values.Any() ? quant.MinPromptToksPerSec.Values.Min() : 0;
                var minEval = quant.MinEvalToksPerSec.Values.Any() ? quant.MinEvalToksPerSec.Values.Min() : 0;
                sb.Append(minPrompt > 0 ? $" {BenchScoring.FormatSpeed(minPrompt)} t/s |" : " - |");
                sb.Append(minEval > 0 ? $" {BenchScoring.FormatSpeed(minEval)} t/s |" : " - |");
                sb.AppendLine();
            }

            sb.AppendLine();

            // Subcategory scores if present (ordered by test suite)
            var orderedSubCategories = GetOrderedSubCategories(resultsFile);
            if (orderedSubCategories.Count > 0)
            {
                sb.AppendLine("## Scores by Subcategory");
                sb.AppendLine();

                // Two-row header for Markdown
                sb.Append("| Model:Tag |");
                string? lastCat = null;
                foreach (var (cat, sub, _) in orderedSubCategories)
                {
                    var catHeader = cat != lastCat ? cat : "";
                    lastCat = cat;
                    sb.Append($" {catHeader} |");
                }
                sb.AppendLine();

                sb.Append("| |");
                foreach (var (_, sub, _) in orderedSubCategories)
                    sb.Append($" {sub} |");
                sb.AppendLine();

                sb.Append("|-----------|");
                foreach (var _ in orderedSubCategories)
                    sb.Append("------|");
                sb.AppendLine();

                foreach (var quant in scoring.QuantScores)
                {
                    sb.Append($"| {quant.Tag} |");
                    foreach (var (_, _, key) in orderedSubCategories)
                    {
                        var score = quant.SubCategoryScores.GetValueOrDefault(key, 0);
                        sb.Append($" {score:F1}% |");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }

            // Average speed table
            sb.AppendLine("## Average Speed per Category");
            sb.AppendLine();

            sb.Append("| Model:Tag | Score |");
            foreach (var cat in categories)
                sb.Append($" {cat} |");
            sb.AppendLine();

            sb.Append("|-----------|-------|");
            foreach (var _ in categories)
                sb.Append("--------|");
            sb.AppendLine();

            var sortedQuants = scoring.QuantScores
                .OrderByDescending(q => q.OverallScore)
                .ToList();

            foreach (var quant in sortedQuants)
            {
                sb.Append($"| {quant.Tag} | {quant.OverallScore:F1}% |");
                foreach (var cat in categories)
                {
                    var avgTime = quant.AvgResponseTimeMs.GetValueOrDefault(cat, 0);
                    sb.Append(avgTime > 0 ? $" {BenchScoring.FormatResponseTime(avgTime)} |" : " - |");
                }
                sb.AppendLine();
            }

            sb.AppendLine();

            // Category statistics
            var catStats = BenchScoring.GetCategoryStatistics(scoring);
            if (catStats.Count > 0)
            {
                sb.AppendLine("## Category Statistics");
                sb.AppendLine();
                sb.AppendLine("| Category | Avg | Min | Max | StdDev |");
                sb.AppendLine("|----------|-----|-----|-----|--------|");

                foreach (var stat in catStats.Values.OrderBy(s => s.Category))
                {
                    sb.AppendLine($"| {stat.Category} | {stat.Average:F1}% | {stat.Min:F1}% | {stat.Max:F1}% | {stat.StdDev:F2} |");
                }
            }

            // Output
            var outputFile = GetOutputFilePath("md");
            if (string.IsNullOrEmpty(_args.OutputFile))
            {
                Console.WriteLine(sb.ToString());
            }
            else
            {
                if (!skipAccessCheck && !CheckOutputFileAccess(outputFile))
                    return;
                await File.WriteAllTextAsync(outputFile, sb.ToString());
                AnsiConsole.MarkupLine($"[green]Markdown saved to: {outputFile}[/]");
            }
        }

        #endregion

        #region HTML Output

        private async Task OutputHtmlAsync(BenchScoringResults scoring, BenchResultsFile resultsFile, bool skipAccessCheck)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head>");
            sb.AppendLine("<meta charset='UTF-8'>");
            sb.AppendLine($"<title>Benchmark Results: {HtmlEncode(scoring.TestType)}</title>");
            sb.AppendLine("<style>");
            sb.AppendLine(@"
        :root {
            --bg-primary: #0d1117;
            --bg-secondary: #161b22;
            --bg-tertiary: #21262d;
            --text-primary: #c9d1d9;
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
        .qa-card.correct { border-left-color: var(--accent-green); }
        .qa-card.incorrect { border-left-color: var(--accent-red); }
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
        .badge-correct { background: var(--accent-green); color: #000; }
        .badge-incorrect { background: var(--accent-red); color: #fff; }
        .badge-score { background: var(--accent-purple); color: #fff; }
        .reason-text { color: var(--text-secondary); font-style: italic; margin: 10px 0; padding: 10px; background: var(--bg-primary); border-radius: 6px; border-left: 3px solid var(--accent-purple); }
        .tools-text { color: var(--text-secondary); font-size: 0.9em; margin: 10px 0; padding: 8px; background: var(--bg-primary); border-radius: 6px; border-left: 3px solid var(--accent-yellow); }
        .thinking-section { margin: 10px 0; padding: 15px; background: var(--bg-primary); border-radius: 8px; border: 1px dashed var(--accent-yellow); }
        .thinking-label { color: var(--accent-yellow); font-weight: 600; margin-bottom: 8px; }
        .thinking-content { color: var(--text-secondary); white-space: pre-wrap; font-family: 'SF Mono', 'Consolas', monospace; font-size: 0.85em; }
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
    ");
            sb.AppendLine("</style>");
            sb.AppendLine("</head><body>");
            sb.AppendLine("<button class=\"theme-toggle\" onclick=\"toggleTheme()\">🌓 Toggle Theme</button>");
            sb.AppendLine("<div class=\"container\">");

            // Header card
            sb.AppendLine("<div class=\"header-card\">");
            sb.AppendLine($"<h1>📊 Benchmark Results: {HtmlEncode(scoring.TestType)}</h1>");
            sb.AppendLine("<div class=\"info-grid\">");
            sb.AppendLine($"<div class=\"info-item\" style=\"grid-column: 1 / -1;\"><span class=\"info-label\">Description</span><span class=\"info-value\">{HtmlEncode(scoring.TestDescription)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Test Suite</span><span class=\"info-value\">{HtmlEncode(scoring.TestSuiteName)}</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Tested</span><span class=\"info-value\">{scoring.TestedAt:yyyy-MM-dd HH:mm:ss} UTC</span></div>");
            sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Total Quants</span><span class=\"info-value\">{scoring.TotalQuants}</span></div>");

            if (!string.IsNullOrEmpty(scoring.JudgeModel))
            {
                var providerBadge = !string.IsNullOrEmpty(scoring.JudgeProvider) && scoring.JudgeProvider != "ollama"
                    ? $"<span class=\"cloud-provider-badge\">{HtmlEncode(scoring.JudgeProvider)}</span>"
                    : "";
                sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Judge Model</span><span class=\"info-value\">{HtmlEncode(scoring.JudgeModel)}{providerBadge}</span></div>");
            }

            var thinkInfoHtml = GetThinkingInfoText(scoring.Options);
            if (!string.IsNullOrEmpty(thinkInfoHtml))
            {
                sb.AppendLine($"<div class=\"info-item\"><span class=\"info-label\">Thinking</span><span class=\"info-value\">{HtmlEncode(thinkInfoHtml)}</span></div>");
            }

            // Version info
            var htmlVersionParts = new List<string>();
            if (!string.IsNullOrEmpty(scoring.OsyncVersion))
                htmlVersionParts.Add($"osync {HtmlEncode(scoring.OsyncVersion)}");
            if (!string.IsNullOrEmpty(scoring.OllamaVersion))
                htmlVersionParts.Add($"Ollama {HtmlEncode(scoring.OllamaVersion)}");
            if (htmlVersionParts.Count > 0)
            {
                sb.AppendLine($"<div class=\"info-item\" style=\"grid-column: 1 / -1;\"><span class=\"info-label\">Versions</span><span class=\"info-value\" style=\"color: var(--text-secondary);\">{string.Join(" | ", htmlVersionParts)}</span></div>");
            }

            sb.AppendLine("</div>"); // Close info-grid
            sb.AppendLine("</div>"); // Close header-card

            var categories = scoring.QuantScores.Count > 0
                ? scoring.QuantScores[0].CategoryScores.Keys.ToList()
                : new List<string>();

            // Scores by Category table
            sb.AppendLine("<h2>📈 Scores by Category</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Model:Tag</th><th>Params</th><th>Size</th><th>Overall</th><th>Correct</th>");
            foreach (var cat in categories)
                sb.AppendLine($"<th>{HtmlEncode(cat)}</th>");
            sb.AppendLine("<th>Min Prompt</th><th>Min Eval</th>");
            sb.AppendLine("</tr></thead>");
            sb.AppendLine("<tbody>");

            foreach (var quant in scoring.QuantScores)
            {
                var scoreClass = GetHtmlScoreClass(quant.OverallScore);
                var paramStr = string.IsNullOrEmpty(quant.ParameterSize) ? "-" : quant.ParameterSize;
                var size = quant.DiskSizeBytes > 0 ? BenchScoring.FormatSize(quant.DiskSizeBytes) : "-";

                sb.AppendLine("<tr>");
                sb.AppendLine($"<td><strong>{HtmlEncode(quant.Tag)}</strong></td>");
                sb.AppendLine($"<td>{HtmlEncode(paramStr)}</td>");
                sb.AppendLine($"<td>{size}</td>");
                sb.AppendLine($"<td class=\"{scoreClass}\">{quant.OverallScore:F1}%</td>");
                sb.AppendLine($"<td>{quant.CorrectAnswers}/{quant.TotalQuestions}</td>");

                foreach (var cat in categories)
                {
                    var catScore = quant.CategoryScores.GetValueOrDefault(cat, 0);
                    var catClass = GetHtmlScoreClass(catScore);
                    sb.AppendLine($"<td class=\"{catClass}\">{catScore:F1}%</td>");
                }

                var minPrompt = quant.MinPromptToksPerSec.Values.Any() ? quant.MinPromptToksPerSec.Values.Min() : 0;
                var minEval = quant.MinEvalToksPerSec.Values.Any() ? quant.MinEvalToksPerSec.Values.Min() : 0;
                sb.AppendLine(minPrompt > 0 ? $"<td>{BenchScoring.FormatSpeed(minPrompt)} t/s</td>" : "<td>-</td>");
                sb.AppendLine(minEval > 0 ? $"<td>{BenchScoring.FormatSpeed(minEval)} t/s</td>" : "<td>-</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");

            // Subcategory scores if present
            var orderedSubCategories = GetOrderedSubCategories(resultsFile);
            if (orderedSubCategories.Count > 0)
            {
                sb.AppendLine("<h2>📊 Scores by Subcategory</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<thead>");

                // Two-row header with category spanning
                sb.AppendLine("<tr><th rowspan=\"2\">Model:Tag</th>");
                var catGroups = orderedSubCategories.GroupBy(x => x.Category).ToList();
                foreach (var group in catGroups)
                {
                    sb.AppendLine($"<th colspan=\"{group.Count()}\">{HtmlEncode(group.Key)}</th>");
                }
                sb.AppendLine("</tr>");

                sb.AppendLine("<tr>");
                foreach (var (_, sub, _) in orderedSubCategories)
                    sb.AppendLine($"<th>{HtmlEncode(sub)}</th>");
                sb.AppendLine("</tr>");
                sb.AppendLine("</thead>");

                sb.AppendLine("<tbody>");
                foreach (var quant in scoring.QuantScores)
                {
                    sb.AppendLine($"<tr><td><strong>{HtmlEncode(quant.Tag)}</strong></td>");
                    foreach (var (_, _, key) in orderedSubCategories)
                    {
                        var score = quant.SubCategoryScores.GetValueOrDefault(key, 0);
                        var scoreClass = GetHtmlScoreClass(score);
                        sb.AppendLine($"<td class=\"{scoreClass}\">{score:F1}%</td>");
                    }
                    sb.AppendLine("</tr>");
                }
                sb.AppendLine("</tbody></table>");
            }

            // Average speed table
            sb.AppendLine("<h2>⏱️ Average Speed per Category</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<thead><tr><th>Model:Tag</th><th>Score</th>");
            foreach (var cat in categories)
                sb.AppendLine($"<th>{HtmlEncode(cat)}</th>");
            sb.AppendLine("</tr></thead>");
            sb.AppendLine("<tbody>");

            var sortedQuants = scoring.QuantScores.OrderByDescending(q => q.OverallScore).ToList();
            foreach (var quant in sortedQuants)
            {
                var scoreClass = GetHtmlScoreClass(quant.OverallScore);
                sb.AppendLine("<tr>");
                sb.AppendLine($"<td><strong>{HtmlEncode(quant.Tag)}</strong></td>");
                sb.AppendLine($"<td class=\"{scoreClass}\">{quant.OverallScore:F1}%</td>");
                foreach (var cat in categories)
                {
                    var avgTime = quant.AvgResponseTimeMs.GetValueOrDefault(cat, 0);
                    sb.AppendLine(avgTime > 0 ? $"<td>{BenchScoring.FormatResponseTime(avgTime)}</td>" : "<td>-</td>");
                }
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</tbody></table>");

            // Detailed Q&A Section (collapsible)
            sb.AppendLine("<h2>💬 Detailed Questions & Answers</h2>");
            OutputHtmlDetails(sb, resultsFile, scoring);

            // Footer
            sb.AppendLine($"<p style=\"text-align:center;color:var(--text-secondary);margin-top:40px;padding:20px;border-top:1px solid var(--border-color);\">Generated by <strong>osync benchview</strong> on {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");

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
            var outputFile = GetOutputFilePath("html");
            if (string.IsNullOrEmpty(_args.OutputFile))
            {
                Console.WriteLine(sb.ToString());
            }
            else
            {
                if (!skipAccessCheck && !CheckOutputFileAccess(outputFile))
                    return;
                await File.WriteAllTextAsync(outputFile, sb.ToString());
                AnsiConsole.MarkupLine($"[green]HTML saved to: {outputFile}[/]");
            }
        }

        private void OutputHtmlDetails(StringBuilder sb, BenchResultsFile resultsFile, BenchScoringResults scoring)
        {
            foreach (var quant in resultsFile.Results.OrderByDescending(r =>
                scoring.QuantScores.FirstOrDefault(q => q.Tag == r.Tag)?.OverallScore ?? 0))
            {
                var quantScore = scoring.QuantScores.FirstOrDefault(q => q.Tag == quant.Tag);
                var scoreDisplay = quantScore != null ? $" — Score: {quantScore.OverallScore:F1}%" : "";

                sb.AppendLine($"<button class=\"collapsible\">{HtmlEncode(quant.Tag)}{scoreDisplay}</button>");
                sb.AppendLine("<div class=\"content\"><div class=\"content-inner\">");

                // Show per-tag metadata if available
                var metaParts = new List<string>();
                if (!string.IsNullOrEmpty(quant.OsyncVersion))
                    metaParts.Add($"osync {HtmlEncode(quant.OsyncVersion)}");
                if (!string.IsNullOrEmpty(quant.OllamaVersion))
                    metaParts.Add($"Ollama {HtmlEncode(quant.OllamaVersion)}");
                if (!string.IsNullOrEmpty(quant.JudgeModel))
                {
                    var judgeStr = HtmlEncode(quant.JudgeModel);
                    if (!string.IsNullOrEmpty(quant.JudgeProvider) && quant.JudgeProvider != "ollama")
                        judgeStr += $" ({HtmlEncode(quant.JudgeProvider)})";
                    metaParts.Add($"Judge: {judgeStr}");
                }
                if (metaParts.Count > 0)
                    sb.AppendLine($"<p style=\"color:var(--text-secondary);font-size:0.85em;margin-bottom:15px;\">{string.Join(" | ", metaParts)}</p>");

                foreach (var cat in quant.CategoryResults ?? new List<BenchCategoryResult>())
                {
                    sb.AppendLine($"<h3>Category: {HtmlEncode(cat.Category)} ({cat.Score:F1}%)</h3>");

                    var questions = GetAllQuestions(cat);
                    foreach (var q in questions)
                    {
                        var isCorrect = q.Score >= 100;
                        var cardClass = isCorrect ? "correct" : "incorrect";
                        var badgeClass = isCorrect ? "badge-correct" : "badge-incorrect";
                        var statusText = isCorrect ? "CORRECT" : "INCORRECT";

                        sb.AppendLine($"<div class=\"qa-card {cardClass}\">");
                        sb.AppendLine($"<div class=\"qa-question\">Q{q.QuestionId}: {HtmlEncode(q.Question)}</div>");

                        // Judgment badges
                        sb.AppendLine($"<span class=\"judgment-badge {badgeClass}\">{statusText}</span>");
                        sb.AppendLine($"<span class=\"judgment-badge badge-score\">Score: {q.Score:F0}%</span>");

                        // Judgment reason
                        if (q.Judgment != null && !string.IsNullOrWhiteSpace(q.Judgment.Reason))
                        {
                            sb.AppendLine($"<div class=\"reason-text\">💭 {HtmlEncode(q.Judgment.Reason)}</div>");
                        }

                        // Tools used
                        if (q.ToolsUsed.Count > 0)
                        {
                            var tools = string.Join(", ", q.ToolsUsed.Select(t => $"{t.ToolName}({t.CallCount})"));
                            sb.AppendLine($"<div class=\"tools-text\">🔧 Tools: {HtmlEncode(tools)}</div>");
                        }

                        // Reference answer
                        sb.AppendLine("<h4>Expected Answer:</h4>");
                        sb.AppendLine($"<div class=\"qa-answer\">{HtmlEncode(q.ReferenceAnswer)}</div>");

                        // Model thinking (if available)
                        if (!string.IsNullOrWhiteSpace(q.ModelThinking))
                        {
                            sb.AppendLine("<div class=\"thinking-section\">");
                            sb.AppendLine("<div class=\"thinking-label\">🧠 Model Thinking:</div>");
                            sb.AppendLine($"<div class=\"thinking-content\">{HtmlEncode(q.ModelThinking)}</div>");
                            sb.AppendLine("</div>");
                        }

                        // Model answer
                        sb.AppendLine("<h4>Model Answer:</h4>");
                        sb.AppendLine($"<div class=\"qa-answer\">{HtmlEncode(q.ModelAnswer)}</div>");

                        sb.AppendLine("</div>"); // Close qa-card
                    }
                }

                sb.AppendLine("</div></div>"); // Close content-inner and content
            }
        }

        private List<BenchQuestionResult> GetAllQuestions(BenchCategoryResult category)
        {
            if (category.QuestionResults != null)
                return category.QuestionResults;

            return category.SubCategoryResults?
                .SelectMany(s => s.QuestionResults)
                .ToList() ?? new List<BenchQuestionResult>();
        }

        private string GetHtmlScoreClass(double score)
        {
            return score >= 75 ? "score-high" : score >= 50 ? "score-mid" : "score-low";
        }

        private string HtmlEncode(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return System.Web.HttpUtility.HtmlEncode(text);
        }

        #endregion

        #region PDF Output

        private async Task OutputPdfAsync(BenchScoringResults scoring, BenchResultsFile resultsFile, ProgressTask? progressTask)
        {
            var outputFile = GetOutputFilePath("pdf");

            if (progressTask == null && !CheckOutputFileAccess(outputFile))
                return;

            progressTask?.Increment(10);

            using var writer = new PdfWriter(outputFile);
            using var pdf = new PdfDocument(writer);
            using var document = new iTextDocument(pdf, PageSize.A4.Rotate());

            var fontRegular = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var fontMono = PdfFontFactory.CreateFont(StandardFonts.COURIER);

            var colorBgLight = new DeviceRgb(248, 249, 250);
            var colorBorder = new DeviceRgb(200, 200, 200);

            // Title
            document.Add(new iTextParagraph($"Benchmark Results: {scoring.TestType}")
                .SetFont(fontBold)
                .SetFontSize(18)
                .SetMarginBottom(15));

            // Header info table (2 columns for key-value pairs)
            var headerTable = new iTextTable(4);
            headerTable.SetWidth(UnitValue.CreatePercentValue(100));
            headerTable.SetBorder(new iText.Layout.Borders.SolidBorder(colorBorder, 1));

            // Helper to add header info cell
            void AddHeaderInfoCell(string label, string value, int colspan = 1)
            {
                var cell = new iTextCell(1, colspan)
                    .SetBackgroundColor(colorBgLight)
                    .SetBorder(new iText.Layout.Borders.SolidBorder(colorBorder, 0.5f))
                    .SetPadding(8);
                var para = new iTextParagraph();
                para.Add(new iTextText(label + ": ").SetFont(fontBold).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY));
                para.Add(new iTextText(value).SetFont(fontRegular).SetFontSize(9));
                cell.Add(para);
                headerTable.AddCell(cell);
            }

            // Row 1: Description (full width)
            var descCell = new iTextCell(1, 4)
                .SetBackgroundColor(colorBgLight)
                .SetBorder(new iText.Layout.Borders.SolidBorder(colorBorder, 0.5f))
                .SetPadding(8);
            var descPara = new iTextParagraph();
            descPara.Add(new iTextText("Description: ").SetFont(fontBold).SetFontSize(9).SetFontColor(ColorConstants.DARK_GRAY));
            descPara.Add(new iTextText(scoring.TestDescription).SetFont(fontRegular).SetFontSize(9));
            descCell.Add(descPara);
            headerTable.AddCell(descCell);

            // Row 2: Test Suite, Tested, Total Quants, Judge Model
            AddHeaderInfoCell("Test Suite", scoring.TestSuiteName);
            AddHeaderInfoCell("Tested", $"{scoring.TestedAt:yyyy-MM-dd HH:mm:ss} UTC");
            AddHeaderInfoCell("Total Quants", scoring.TotalQuants.ToString());

            var judgeValue = scoring.JudgeModel ?? "-";
            if (!string.IsNullOrEmpty(scoring.JudgeProvider) && scoring.JudgeProvider != "ollama")
                judgeValue += $" ({scoring.JudgeProvider})";
            AddHeaderInfoCell("Judge Model", judgeValue);

            // Row 3: Thinking, osync version, Ollama version
            var thinkInfoPdf = GetThinkingInfoText(scoring.Options);
            AddHeaderInfoCell("Thinking", string.IsNullOrEmpty(thinkInfoPdf) ? "disabled" : thinkInfoPdf);
            AddHeaderInfoCell("osync", scoring.OsyncVersion ?? "-");
            AddHeaderInfoCell("Ollama", scoring.OllamaVersion ?? "-");

            // Empty cell to complete the row
            headerTable.AddCell(new iTextCell()
                .SetBackgroundColor(colorBgLight)
                .SetBorder(new iText.Layout.Borders.SolidBorder(colorBorder, 0.5f)));

            document.Add(headerTable);
            document.Add(new iTextParagraph("").SetMarginBottom(15));

            progressTask?.Increment(20);

            // Results table
            var categories = scoring.QuantScores.Count > 0
                ? scoring.QuantScores[0].CategoryScores.Keys.ToList()
                : new List<string>();

            // Scores by Category table: Model:Tag, Params, Size, Overall, Correct, categories..., Min Prompt, Min Eval
            var numColumns = 7 + categories.Count; // 5 base + categories + 2 speed cols
            var table = new iTextTable(numColumns);
            table.SetWidth(UnitValue.CreatePercentValue(100));

            // Header
            AddPdfHeaderCell(table, "Model:Tag", fontBold);
            AddPdfHeaderCell(table, "Params", fontBold);
            AddPdfHeaderCell(table, "Size", fontBold);
            AddPdfHeaderCell(table, "Overall", fontBold);
            AddPdfHeaderCell(table, "Correct", fontBold);
            foreach (var cat in categories)
                AddPdfHeaderCell(table, cat, fontBold);
            AddPdfHeaderCell(table, "Min Prompt", fontBold);
            AddPdfHeaderCell(table, "Min Eval", fontBold);

            progressTask?.Increment(10);

            // Data rows
            foreach (var quant in scoring.QuantScores)
            {
                var paramStr = string.IsNullOrEmpty(quant.ParameterSize) ? "-" : quant.ParameterSize;
                var size = quant.DiskSizeBytes > 0 ? BenchScoring.FormatSize(quant.DiskSizeBytes) : "-";

                AddPdfCell(table, quant.Tag, fontMono, 8);
                AddPdfCell(table, paramStr, fontRegular);
                AddPdfCell(table, size, fontRegular);
                AddPdfScoreCell(table, quant.OverallScore, fontBold);
                AddPdfCell(table, $"{quant.CorrectAnswers}/{quant.TotalQuestions}", fontRegular);

                foreach (var cat in categories)
                {
                    var catScore = quant.CategoryScores.GetValueOrDefault(cat, 0);
                    AddPdfScoreCell(table, catScore, fontRegular);
                }

                var minPrompt = quant.MinPromptToksPerSec.Values.Any() ? quant.MinPromptToksPerSec.Values.Min() : 0;
                var minEval = quant.MinEvalToksPerSec.Values.Any() ? quant.MinEvalToksPerSec.Values.Min() : 0;
                AddPdfCell(table, minPrompt > 0 ? $"{BenchScoring.FormatSpeed(minPrompt)} t/s" : "-", fontRegular);
                AddPdfCell(table, minEval > 0 ? $"{BenchScoring.FormatSpeed(minEval)} t/s" : "-", fontRegular);
            }

            document.Add(table);
            progressTask?.Increment(10);

            // Subcategory scores table (if subcategories exist)
            var orderedSubCategories = GetOrderedSubCategories(resultsFile);
            if (orderedSubCategories.Count > 0)
            {
                document.Add(new iTextParagraph("Scores by Subcategory")
                    .SetFont(fontBold)
                    .SetFontSize(12)
                    .SetMarginTop(20)
                    .SetMarginBottom(10));

                var subCatGroups = orderedSubCategories.GroupBy(x => x.Category).ToList();
                var subCatColCount = orderedSubCategories.Count + 1; // +1 for Model:Tag column
                var subTable = new iTextTable(subCatColCount);
                subTable.SetWidth(UnitValue.CreatePercentValue(100));

                // Two-row header: first row with category spanning, second row with subcategories
                // First row: Model:Tag + category headers with colspan
                var tagCell = new iTextCell(2, 1)
                    .Add(new iTextParagraph("Model:Tag").SetFont(fontBold).SetFontSize(9))
                    .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                    .SetPadding(5);
                subTable.AddHeaderCell(tagCell);

                foreach (var group in subCatGroups)
                {
                    var catCell = new iTextCell(1, group.Count())
                        .Add(new iTextParagraph(group.Key).SetFont(fontBold).SetFontSize(9))
                        .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                        .SetPadding(5)
                        .SetTextAlignment(TextAlignment.CENTER);
                    subTable.AddHeaderCell(catCell);
                }

                // Second row: subcategory names
                foreach (var (_, sub, _) in orderedSubCategories)
                {
                    AddPdfHeaderCell(subTable, sub, fontBold);
                }

                // Data rows
                foreach (var quant in scoring.QuantScores)
                {
                    AddPdfCell(subTable, quant.Tag, fontMono, 8);
                    foreach (var (_, _, key) in orderedSubCategories)
                    {
                        var score = quant.SubCategoryScores.GetValueOrDefault(key, 0);
                        AddPdfScoreCell(subTable, score, fontRegular);
                    }
                }

                document.Add(subTable);
            }

            progressTask?.Increment(5);

            // Average Speed per Category table
            document.Add(new iTextParagraph("Average Speed per Category")
                .SetFont(fontBold)
                .SetFontSize(12)
                .SetMarginTop(20)
                .SetMarginBottom(10));

            var speedColCount = categories.Count + 2; // Model:Tag, Score, + categories
            var speedTable = new iTextTable(speedColCount);
            speedTable.SetWidth(UnitValue.CreatePercentValue(100));

            // Header
            AddPdfHeaderCell(speedTable, "Model:Tag", fontBold);
            AddPdfHeaderCell(speedTable, "Score", fontBold);
            foreach (var cat in categories)
                AddPdfHeaderCell(speedTable, cat, fontBold);

            // Data rows (sorted by overall score)
            var sortedQuantsForSpeed = scoring.QuantScores
                .OrderByDescending(q => q.OverallScore)
                .ToList();

            foreach (var quant in sortedQuantsForSpeed)
            {
                AddPdfCell(speedTable, quant.Tag, fontMono, 8);
                AddPdfScoreCell(speedTable, quant.OverallScore, fontBold);
                foreach (var cat in categories)
                {
                    var avgTime = quant.AvgResponseTimeMs.GetValueOrDefault(cat, 0);
                    AddPdfCell(speedTable, avgTime > 0 ? BenchScoring.FormatResponseTime(avgTime) : "-", fontRegular);
                }
            }

            document.Add(speedTable);
            progressTask?.Increment(5);

            // Detailed Q&A Results
            document.Add(new AreaBreak());
            document.Add(new iTextParagraph("Detailed Q&A Results")
                .SetFont(fontBold)
                .SetFontSize(14)
                .SetMarginBottom(10));

            OutputPdfDetails(document, resultsFile, fontRegular, fontBold, fontMono);

            progressTask?.Increment(20);

            document.Close();
            AnsiConsole.MarkupLine($"[green]PDF saved to: {outputFile}[/]");
        }

        private void AddPdfHeaderCell(iTextTable table, string text, PdfFont font)
        {
            var borderColor = new DeviceRgb(180, 180, 180);
            var cell = new iTextCell()
                .Add(new iTextParagraph(text).SetFont(font).SetFontSize(9))
                .SetBackgroundColor(new DeviceRgb(240, 240, 240))
                .SetBorder(new iText.Layout.Borders.SolidBorder(borderColor, 1))
                .SetPadding(5);
            table.AddHeaderCell(cell);
        }

        private void AddPdfCell(iTextTable table, string text, PdfFont font, int fontSize = 9)
        {
            var borderColor = new DeviceRgb(220, 220, 220);
            var cell = new iTextCell()
                .Add(new iTextParagraph(text).SetFont(font).SetFontSize(fontSize))
                .SetBorder(new iText.Layout.Borders.SolidBorder(borderColor, 0.5f))
                .SetPadding(4);
            table.AddCell(cell);
        }

        private void AddPdfScoreCell(iTextTable table, double score, PdfFont font)
        {
            var color = score >= 75
                ? new DeviceRgb(212, 237, 218)
                : score >= 50
                    ? new DeviceRgb(255, 243, 205)
                    : new DeviceRgb(248, 215, 218);
            var borderColor = new DeviceRgb(220, 220, 220);

            var cell = new iTextCell()
                .Add(new iTextParagraph($"{score:F1}%").SetFont(font).SetFontSize(9))
                .SetBackgroundColor(color)
                .SetBorder(new iText.Layout.Borders.SolidBorder(borderColor, 0.5f))
                .SetPadding(4)
                .SetTextAlignment(TextAlignment.RIGHT);
            table.AddCell(cell);
        }

        private void OutputPdfDetails(iTextDocument document, BenchResultsFile resultsFile, PdfFont fontRegular, PdfFont fontBold, PdfFont fontMono)
        {
            var colorGreen = new DeviceRgb(40, 167, 69);
            var colorRed = new DeviceRgb(220, 53, 69);
            var colorPurple = new DeviceRgb(111, 66, 193);
            var colorYellow = new DeviceRgb(210, 153, 34);
            var colorBgGray = new DeviceRgb(248, 249, 250);

            foreach (var quant in resultsFile.Results)
            {
                // Quant header
                document.Add(new iTextParagraph(quant.Tag)
                    .SetFont(fontBold)
                    .SetFontSize(14)
                    .SetMarginTop(20)
                    .SetMarginBottom(5));

                // Per-tag metadata
                var metaParts = new List<string>();
                if (!string.IsNullOrEmpty(quant.OsyncVersion))
                    metaParts.Add($"osync {quant.OsyncVersion}");
                if (!string.IsNullOrEmpty(quant.OllamaVersion))
                    metaParts.Add($"Ollama {quant.OllamaVersion}");
                if (!string.IsNullOrEmpty(quant.JudgeModel))
                {
                    var judgeStr = quant.JudgeModel;
                    if (!string.IsNullOrEmpty(quant.JudgeProvider) && quant.JudgeProvider != "ollama")
                        judgeStr += $" ({quant.JudgeProvider})";
                    metaParts.Add($"Judge: {judgeStr}");
                }
                if (metaParts.Count > 0)
                {
                    document.Add(new iTextParagraph(string.Join(" | ", metaParts))
                        .SetFont(fontRegular)
                        .SetFontSize(8)
                        .SetFontColor(ColorConstants.GRAY)
                        .SetMarginBottom(10));
                }

                foreach (var cat in quant.CategoryResults ?? new List<BenchCategoryResult>())
                {
                    document.Add(new iTextParagraph($"Category: {cat.Category} ({cat.Score:F1}%)")
                        .SetFont(fontBold)
                        .SetFontSize(11)
                        .SetMarginTop(15)
                        .SetMarginBottom(8));

                    var questions = GetAllQuestions(cat);
                    foreach (var q in questions)
                    {
                        var isCorrect = q.Score >= 100;
                        var statusText = isCorrect ? "CORRECT" : "INCORRECT";
                        var statusColor = isCorrect ? colorGreen : colorRed;

                        // Question header with status
                        var headerPara = new iTextParagraph();
                        headerPara.Add(new iTextText($"Q{q.QuestionId}: ").SetFont(fontBold).SetFontSize(10));
                        headerPara.Add(new iTextText($"[{statusText}]").SetFont(fontBold).SetFontSize(10).SetFontColor(statusColor));
                        headerPara.Add(new iTextText($" (Score: {q.Score:F0}%)").SetFont(fontRegular).SetFontSize(9).SetFontColor(ColorConstants.GRAY));
                        headerPara.SetMarginTop(10);
                        document.Add(headerPara);

                        // Question text
                        document.Add(CreateCodeParagraph($"Question: {SanitizePdfText(q.Question)}", fontMono, 8, colorBgGray));

                        // Judgment reason
                        if (q.Judgment != null && !string.IsNullOrWhiteSpace(q.Judgment.Reason))
                        {
                            var reasonPara = new iTextParagraph();
                            reasonPara.Add(new iTextText("Judge: ").SetFont(fontBold).SetFontSize(8).SetFontColor(colorPurple));
                            reasonPara.Add(new iTextText(SanitizePdfText(q.Judgment.Reason)).SetFont(fontRegular).SetFontSize(8).SetFontColor(ColorConstants.DARK_GRAY));
                            reasonPara.SetMarginTop(5);
                            document.Add(reasonPara);
                        }

                        // Tools used
                        if (q.ToolsUsed.Count > 0)
                        {
                            var tools = string.Join(", ", q.ToolsUsed.Select(t => $"{t.ToolName}({t.CallCount})"));
                            var toolsPara = new iTextParagraph();
                            toolsPara.Add(new iTextText("Tools: ").SetFont(fontBold).SetFontSize(8).SetFontColor(colorYellow));
                            toolsPara.Add(new iTextText(tools).SetFont(fontRegular).SetFontSize(8));
                            toolsPara.SetMarginTop(3);
                            document.Add(toolsPara);
                        }

                        // Expected answer
                        document.Add(new iTextParagraph("Expected Answer:")
                            .SetFont(fontBold)
                            .SetFontSize(9)
                            .SetMarginTop(8)
                            .SetFontColor(colorGreen));
                        document.Add(CreateCodeParagraph(SanitizePdfText(q.ReferenceAnswer), fontMono, 8, colorBgGray));

                        // Model thinking (if available)
                        if (!string.IsNullOrWhiteSpace(q.ModelThinking))
                        {
                            document.Add(new iTextParagraph("Model Thinking:")
                                .SetFont(fontBold)
                                .SetFontSize(9)
                                .SetMarginTop(8)
                                .SetFontColor(colorYellow));
                            document.Add(CreateCodeParagraph(SanitizePdfText(q.ModelThinking), fontMono, 7, new DeviceRgb(255, 248, 220)));
                        }

                        // Model answer
                        document.Add(new iTextParagraph("Model Answer:")
                            .SetFont(fontBold)
                            .SetFontSize(9)
                            .SetMarginTop(8)
                            .SetFontColor(new DeviceRgb(88, 166, 255)));
                        document.Add(CreateCodeParagraph(SanitizePdfText(q.ModelAnswer), fontMono, 8, colorBgGray));
                    }
                }

                // Page break between quants for better readability
                document.Add(new AreaBreak());
            }
        }

        /// <summary>
        /// Create a paragraph for code/answer content to avoid PDF text corruption issues.
        /// </summary>
        private iTextParagraph CreateCodeParagraph(string text, PdfFont font, int fontSize, DeviceRgb bgColor)
        {
            var para = new iTextParagraph();

            // Add text line by line to avoid corruption issues with long text
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                para.Add(new iTextText(lines[i]).SetFont(font).SetFontSize(fontSize));
                if (i < lines.Length - 1)
                    para.Add(new iTextText("\n").SetFont(font).SetFontSize(fontSize));
            }

            para.SetBackgroundColor(bgColor)
                .SetPadding(8)
                .SetMarginTop(3)
                .SetMarginBottom(3);

            return para;
        }

        /// <summary>
        /// Sanitize text for PDF output to avoid character corruption.
        /// </summary>
        private string SanitizePdfText(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";

            // Replace problematic Unicode characters with ASCII equivalents
            return text
                .Replace("\u2018", "'")  // Left single quote
                .Replace("\u2019", "'")  // Right single quote
                .Replace("\u201C", "\"") // Left double quote
                .Replace("\u201D", "\"") // Right double quote
                .Replace("\u2013", "-")  // En dash
                .Replace("\u2014", "--") // Em dash
                .Replace("\u2026", "...") // Ellipsis
                .Replace("\u00A0", " "); // Non-breaking space
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Get display text for thinking settings
        /// </summary>
        private string GetThinkingInfoText(BenchTestOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.ThinkLevel))
                return $"level={options.ThinkLevel}";
            if (options.EnableThinking)
                return "enabled";
            return "disabled";
        }

        #endregion
    }
}
