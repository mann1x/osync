using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace osync
{
    /// <summary>
    /// Implementation of the qc (Quants Compare) command
    /// Runs test suite on model quantizations and captures logprobs for comparison
    /// </summary>
    public class QcCommand
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly QcArgs _args;
        private ITestSuite _testSuite;
        private QcResultsFile _resultsFile;

        // Judge model fields
        private HttpClient? _judgeHttpClient;
        private string? _judgeBaseUrl;
        private string? _judgeModelName;
        private bool _judgeEnabled;

        // Cancellation support for graceful shutdown
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private bool _cancellationRequested;

        // Track partial quantization being tested (for saving on cancellation)
        private QuantResult? _currentPartialResult;

        // Retry configuration for API calls
        private const int MAX_RETRY_ATTEMPTS = 5;
        private const int RETRY_DELAY_MS = 1000;

        // System prompt for the judge model
        private const string JUDGE_SYSTEM_PROMPT = @"You are evaluating the similarity between two LLM responses to the same question.

Compare the ""quantized"" response against the ""base"" response and provide a similarity score.

CRITICAL: Your score MUST be an integer between 1 and 100. Never use 0 or negative numbers.

Scoring criteria:
- 90-100: Responses are essentially identical in meaning and correctness
- 70-89: Minor differences but same core answer and logic
- 50-69: Noticeable differences but still reasonably similar
- 30-49: Significant differences in answer or approach
- 10-29: Fundamentally different or incorrect compared to base
- 1-9: Completely unrelated or nonsensical responses

Important:
- Do NOT evaluate correctness of the base answer - only compare similarity
- Verbosity differences are acceptable if the core answer is the same
- Focus on whether the quantized version provides equivalent information
- Even if both responses are poor quality, rate their SIMILARITY to each other
- If responses are identical gibberish, they are still 90-100 similar

Provide your score (1-100, NEVER 0) and a brief reason explaining your evaluation.";

        public QcCommand(QcArgs args, string baseUrl = "http://localhost:11434")
        {
            _args = args;
            _baseUrl = baseUrl.TrimEnd('/');

            // Apply default timeout if not set (PowerArgs may not apply default value)
            if (_args.Timeout <= 0)
                _args.Timeout = 600;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_args.Timeout)
            };

            // Apply default values if not set (PowerArgs doesn't use property initializers)
            // Since all defaults except TopK are non-zero, we can detect if they weren't set
            if (_args.Seed == 0)
                _args.Seed = 365;
            if (_args.TopP == 0)
                _args.TopP = 0.001;
            if (_args.TopK == 0)
                _args.TopK = -1;
            // Note: BaseTag default is handled in ExecuteAsync after loading results file

            // Initialize judge if specified
            if (!string.IsNullOrEmpty(_args.Judge))
            {
                ParseJudgeArgument(_args.Judge);
            }
        }

        /// <summary>
        /// Parse the --judge argument to extract base URL and model name
        /// Supports: "modelname", "http://host:port/modelname"
        /// </summary>
        private void ParseJudgeArgument(string judge)
        {
            if (judge.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                judge.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Remote judge: http://host:port/modelname
                var match = Regex.Match(judge, @"^(https?://[^/]+)/(.+)$", RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    _judgeBaseUrl = match.Groups[1].Value;
                    _judgeModelName = match.Groups[2].Value;
                }
                else
                {
                    // URL without model name - invalid
                    AnsiConsole.MarkupLine($"[yellow]Warning: Invalid judge URL format. Expected http://host:port/modelname[/]");
                    return;
                }
            }
            else
            {
                // Local judge: just model name, use local server
                _judgeBaseUrl = "http://localhost:11434";
                _judgeModelName = judge;
            }

            // Add :latest if no tag specified
            if (!_judgeModelName.Contains(':'))
            {
                _judgeModelName += ":latest";
            }

            _judgeHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_args.Timeout)
            };

            _judgeEnabled = true;
            AnsiConsole.MarkupLine($"[dim]Judge model: {_judgeModelName} @ {_judgeBaseUrl}[/]");
        }

        /// <summary>
        /// Check if running in serial judgment mode (default)
        /// </summary>
        private bool IsSerialMode()
        {
            return string.IsNullOrEmpty(_args.JudgeMode) ||
                   _args.JudgeMode.Equals("serial", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if running in parallel judgment mode
        /// </summary>
        private bool IsParallelMode()
        {
            return _args.JudgeMode?.Equals("parallel", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Check if a quantization result needs judgment
        /// Returns true if: no judgment exists, or judgment was done with a different model
        /// </summary>
        private bool NeedsJudgment(QuantResult quantResult)
        {
            if (!_judgeEnabled || _judgeModelName == null)
                return false;

            // Check each question - if ANY question is missing judgment or has different judge model, needs re-judgment
            foreach (var question in quantResult.QuestionResults)
            {
                if (question.Judgment == null)
                {
                    // No judgment at all
                    return true;
                }

                if (question.Judgment.JudgeModel != _judgeModelName)
                {
                    // Different judge model was used
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Main execution method for qc command
        /// </summary>
        public async Task<int> ExecuteAsync()
        {
            // Set up Ctrl+C/Ctrl+Break handler for graceful shutdown
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                // Initialize test suite
                if (!LoadTestSuite())
                    return 1;

                // Initialize or load results file
                if (!await InitializeResultsFileAsync())
                    return 1;

                // Print output file path
                AnsiConsole.MarkupLine($"[dim]Output file: {GetOutputFilePath()}[/]");

                // Parse quantization tags to test
                var quantTags = _args.Quants.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(q => q.Trim())
                    .ToList();

                if (quantTags.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: No quantization tags specified[/]");
                    return 1;
                }

                // Verify judge model exists at startup (if enabled)
                if (_judgeEnabled)
                {
                    AnsiConsole.MarkupLine($"[dim]Verifying judge model exists...[/]");
                    if (!await VerifyJudgeModelExistsAsync())
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Judge model '{_judgeModelName}' not found on server {_judgeBaseUrl}[/]");
                        AnsiConsole.MarkupLine($"[yellow]Make sure the judge model is pulled. Try: ollama pull {_judgeModelName}[/]");
                        return 1;
                    }
                    AnsiConsole.MarkupLine($"[dim]Judge model verified: {_judgeModelName}[/]");
                }

                // Check if we have a base model in results
                var existingBase = _resultsFile.Results.FirstOrDefault(r => r.IsBase);

                // Determine the base tag to use
                if (existingBase != null)
                {
                    // Use the existing base tag from results file
                    _args.BaseTag = existingBase.Tag;
                }
                else if (string.IsNullOrEmpty(_args.BaseTag))
                {
                    // No base in results and no -B specified, default to fp16
                    _args.BaseTag = "fp16";
                }
                else if (_args.BaseTag.Contains(':'))
                {
                    // BaseTag contains a full model name, extract just the tag portion for comparison
                    _args.BaseTag = _args.BaseTag.Substring(_args.BaseTag.LastIndexOf(':') + 1);
                }

                // Ensure base tag is included if not already present and no base exists yet
                if (existingBase == null && !quantTags.Contains(_args.BaseTag))
                {
                    quantTags.Insert(0, _args.BaseTag);
                }

                // Track quantizations that need judgment (existing but missing/different judge)
                var quantsNeedingJudgment = new List<QuantResult>();

                // Track all background judgment tasks (parallel mode only)
                var allBackgroundJudgmentTasks = new ConcurrentDictionary<string, (List<Task> tasks, int totalQuestions)>();

                // Test each quantization
                foreach (var tag in quantTags)
                {
                    // Check for cancellation at the start of each quantization
                    if (_cancellationRequested)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                    }

                    // Construct full model name: use tag as-is if it contains ":", otherwise append to ModelName
                    string modelFullName;
                    string tagForTracking;

                    if (tag.Contains(':'))
                    {
                        // User provided full model name like "qwen2:0.5b-instruct-q8_0"
                        modelFullName = tag;
                        // Extract tag portion after last ":"
                        tagForTracking = tag.Substring(tag.LastIndexOf(':') + 1);
                    }
                    else
                    {
                        // User provided just tag like "q8_0" or "0.5b-instruct-q8_0"
                        modelFullName = $"{_args.ModelName}:{tag}";
                        tagForTracking = tag;
                    }

                    // Check if already tested (complete or partial)
                    var existingResult = _resultsFile.Results.FirstOrDefault(r => r?.Tag == tagForTracking);
                    if (existingResult != null)
                    {
                        // Check if this is a complete result
                        var isComplete = existingResult.QuestionResults.Count >= _testSuite.TotalQuestions;

                        if (isComplete && !_args.Force)
                        {
                            AnsiConsole.MarkupLine($"[yellow]Skipping {modelFullName} (already tested, use --force to re-run)[/]");

                            // Check if this existing result needs judgment
                            if (_judgeEnabled && !existingResult.IsBase && NeedsJudgment(existingResult))
                            {
                                quantsNeedingJudgment.Add(existingResult);
                            }
                            continue;
                        }
                        else if (!isComplete && !_args.Force)
                        {
                            // Partial result - resume testing
                            var answeredCount = existingResult.QuestionResults.Count;
                            AnsiConsole.MarkupLine($"[cyan]Resuming {modelFullName} ({answeredCount}/{_testSuite.TotalQuestions} questions completed)[/]");
                        }
                        else if (_args.Force)
                        {
                            // Remove existing result to re-run
                            _resultsFile.Results.Remove(existingResult);
                            existingResult = null;
                            AnsiConsole.MarkupLine($"[yellow]Re-running {modelFullName} (forced)[/]");
                        }
                    }

                    // Only print "Testing quantization" if not resuming
                    if (existingResult == null)
                    {
                        AnsiConsole.MarkupLine($"[cyan]Testing quantization: {modelFullName}[/]");
                    }

                    // Get model metadata
                    var modelInfo = await GetModelInfoAsync(modelFullName);
                    if (modelInfo == null)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Could not retrieve model info for {modelFullName}[/]");
                        AnsiConsole.MarkupLine($"[yellow]Make sure the model exists. Try: osync list {_args.ModelName}*[/]");
                        continue;
                    }

                    // Validate against base model if not base
                    if (tagForTracking != _args.BaseTag && _resultsFile.Results.Count > 0)
                    {
                        var baseResult = _resultsFile.Results.FirstOrDefault(r => r.IsBase);
                        if (baseResult != null)
                        {
                            if (modelInfo.Family != baseResult.Family ||
                                modelInfo.ParameterSize != baseResult.ParameterSize)
                            {
                                AnsiConsole.MarkupLine($"[red]Error: Model mismatch![/]");
                                AnsiConsole.MarkupLine($"  Expected: {baseResult.Family} {baseResult.ParameterSize}");
                                AnsiConsole.MarkupLine($"  Got: {modelInfo.Family} {modelInfo.ParameterSize}");
                                continue;
                            }
                        }
                    }

                    // Preload model
                    AnsiConsole.MarkupLine($"[dim]Preloading model...[/]");
                    if (!await PreloadModelAsync(modelFullName))
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Failed to preload {modelFullName}[/]");
                        continue;
                    }

                    // Run test suite - in parallel mode, pass base result for concurrent judgment
                    QuantResult? baseResultForParallel = null;
                    if (_judgeEnabled && IsParallelMode() && tagForTracking != _args.BaseTag)
                    {
                        baseResultForParallel = _resultsFile.Results.FirstOrDefault(r => r.IsBase);
                    }

                    // Pass existing partial result for resume functionality
                    var (quantResult, judgmentTasks) = await RunTestSuiteAsync(modelFullName, tagForTracking, modelInfo, baseResultForParallel, existingResult);
                    if (quantResult != null)
                    {
                        quantResult.IsBase = (tagForTracking == _args.BaseTag);

                        // If resuming, remove old partial result before adding the completed one
                        if (existingResult != null)
                        {
                            _resultsFile.Results.Remove(existingResult);
                        }
                        _resultsFile.Results.Add(quantResult);

                        // Save after each quantization (testing complete, judgment may still be running)
                        SaveResultsFile();
                        AnsiConsole.MarkupLine($"[green]✓ Completed testing {modelFullName}[/]");

                        // Track background judgment tasks for parallel mode
                        if (judgmentTasks.Count > 0)
                        {
                            allBackgroundJudgmentTasks[tagForTracking] = (judgmentTasks, judgmentTasks.Count);
                            AnsiConsole.MarkupLine($"[dim]  Judgment for {tagForTracking} running in background ({judgmentTasks.Count} questions)...[/]");
                        }

                        // Run serial judgment if enabled and not base model
                        if (_judgeEnabled && !quantResult.IsBase && IsSerialMode())
                        {
                            var baseResult = _resultsFile.Results.FirstOrDefault(r => r.IsBase);
                            if (baseResult != null)
                            {
                                await RunJudgmentSerialAsync(quantResult, baseResult);
                            }
                        }
                    }
                }

                // Run judgment for existing quantizations that need it
                if (_judgeEnabled && quantsNeedingJudgment.Count > 0)
                {
                    var baseResult = _resultsFile.Results.FirstOrDefault(r => r.IsBase);
                    if (baseResult != null)
                    {
                        if (IsSerialMode())
                        {
                            AnsiConsole.MarkupLine($"\n[cyan]Running judgment for {quantsNeedingJudgment.Count} existing quantization(s)...[/]");
                            foreach (var quantResult in quantsNeedingJudgment)
                            {
                                await RunJudgmentSerialAsync(quantResult, baseResult);
                            }
                        }
                        else if (IsParallelMode())
                        {
                            // Run parallel judgment for existing quantizations with progress tracking
                            AnsiConsole.MarkupLine($"\n[magenta]Running parallel judgment for {quantsNeedingJudgment.Count} existing quantization(s)...[/]");
                            foreach (var quantResult in quantsNeedingJudgment)
                            {
                                await RunJudgmentParallelAsync(quantResult, baseResult);
                            }
                        }
                    }
                }

                // Wait for all background judgment tasks from new quantizations (parallel mode)
                if (allBackgroundJudgmentTasks.Count > 0)
                {
                    await WaitForBackgroundJudgmentsAsync(allBackgroundJudgmentTasks);

                    // Save final results with all judgments complete
                    SaveResultsFile();
                }

                // Final summary
                AnsiConsole.MarkupLine($"\n[green]Testing complete![/]");

                // Check if any quantizations were successfully tested
                if (_resultsFile.Results.Count > 0)
                {
                    AnsiConsole.MarkupLine($"Results saved to: [cyan]{GetOutputFilePath()}[/]");
                    AnsiConsole.MarkupLine($"Successfully tested {_resultsFile.Results.Count} quantization(s)");
                    AnsiConsole.MarkupLine($"View results with: [yellow]osync qcview {GetOutputFilePath()}[/]");
                    return 0;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]No quantizations were successfully tested - no results file created[/]");
                    return 1;
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown requested (includes TaskCanceledException)
                AnsiConsole.MarkupLine($"\n[yellow]Operation cancelled by user[/]");
                SavePartialResults();
                return 2;
            }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException)
            {
                // Cancellation wrapped in another exception (e.g., from Spectre.Console Progress)
                AnsiConsole.MarkupLine($"\n[yellow]Operation cancelled by user[/]");
                SavePartialResults();
                return 2;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                SavePartialResults();
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
            }
        }

        /// <summary>
        /// Handle Ctrl+C/Ctrl+Break for graceful shutdown
        /// </summary>
        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            if (_cancellationRequested)
            {
                // Second Ctrl+C - force exit
                AnsiConsole.MarkupLine($"\n[red]Force exiting...[/]");
                return;
            }

            // First Ctrl+C - request graceful cancellation
            e.Cancel = true;
            _cancellationRequested = true;
            _cts.Cancel();
            AnsiConsole.MarkupLine($"\n[yellow]Cancellation requested. Saving partial results... (press Ctrl+C again to force exit)[/]");
        }

        /// <summary>
        /// Save partial results if any exist
        /// </summary>
        private void SavePartialResults()
        {
            if (_resultsFile == null)
                return;

            // Add partial quantization if it has any results
            if (_currentPartialResult != null && _currentPartialResult.QuestionResults.Count > 0)
            {
                // Check if this quantization is already in results (shouldn't happen, but be safe)
                var existing = _resultsFile.Results.FirstOrDefault(r => r.Tag == _currentPartialResult.Tag);
                if (existing == null)
                {
                    _resultsFile.Results.Add(_currentPartialResult);
                }
                else
                {
                    // Merge question results - keep the ones we have
                    foreach (var qr in _currentPartialResult.QuestionResults)
                    {
                        if (!existing.QuestionResults.Any(eq => eq.QuestionId == qr.QuestionId))
                        {
                            existing.QuestionResults.Add(qr);
                        }
                    }
                }
            }

            // Check if we have any results to save
            if (_resultsFile.Results.Count > 0)
            {
                SaveResultsFile();
                var testedCount = _resultsFile.Results.Count;
                var partialCount = _resultsFile.Results.Count(r => r.QuestionResults.Count < _testSuite.TotalQuestions);
                AnsiConsole.MarkupLine($"[dim]Partial results saved to: {GetOutputFilePath()}[/]");
                if (partialCount > 0)
                {
                    AnsiConsole.MarkupLine($"[dim]{testedCount} quantization(s) saved ({partialCount} incomplete)[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[dim]{testedCount} quantization(s) saved[/]");
                }
                AnsiConsole.MarkupLine($"[dim]Resume with the same command to continue from where you left off.[/]");
            }
        }

        /// <summary>
        /// Check if there's a partial quantization being tested (not yet added to Results)
        /// </summary>
        private bool HasPartialQuantization()
        {
            return _currentPartialResult != null && _currentPartialResult.QuestionResults.Count > 0;
        }

        /// <summary>
        /// Repair IsBase flag for existing results files that may have incorrect base marking
        /// </summary>
        private void RepairBaseFlag()
        {
            if (_resultsFile == null || _resultsFile.Results.Count == 0)
                return;

            // Check if there's already a base marked
            var existingBase = _resultsFile.Results.FirstOrDefault(r => r.IsBase);
            if (existingBase != null)
                return; // Already have a base, no repair needed

            // No base marked - try to identify and fix
            // Extract the base tag from _args.BaseTag (normalize if it contains full model name)
            var baseTag = _args.BaseTag;
            if (!string.IsNullOrEmpty(baseTag) && baseTag.Contains(':'))
            {
                baseTag = baseTag.Substring(baseTag.LastIndexOf(':') + 1);
            }

            if (string.IsNullOrEmpty(baseTag))
                return;

            // Find the matching result and mark it as base
            var matchingResult = _resultsFile.Results.FirstOrDefault(r => r.Tag == baseTag);
            if (matchingResult != null)
            {
                matchingResult.IsBase = true;
                AnsiConsole.MarkupLine($"[dim]Repaired base flag for: {matchingResult.Tag}[/]");
            }
        }

        /// <summary>
        /// Load test suite (internal v1base or external JSON)
        /// </summary>
        private bool LoadTestSuite()
        {
            if (string.IsNullOrEmpty(_args.TestSuite))
            {
                // Use internal v1base (default)
                _testSuite = new V1BaseTestSuite();
                PrintTestSuiteInfo();
                return true;
            }

            // Check for internal test suites
            if (_args.TestSuite.Equals("v1quick", StringComparison.OrdinalIgnoreCase))
            {
                _testSuite = new V1QuickTestSuite();
                PrintTestSuiteInfo();
                return true;
            }

            if (_args.TestSuite.Equals("v1base", StringComparison.OrdinalIgnoreCase))
            {
                _testSuite = new V1BaseTestSuite();
                PrintTestSuiteInfo();
                return true;
            }

            if (_args.TestSuite.Equals("v1code", StringComparison.OrdinalIgnoreCase))
            {
                _testSuite = new V1CodeTestSuite();
                PrintTestSuiteInfo();
                return true;
            }

            // Load external test suite
            try
            {
                if (!File.Exists(_args.TestSuite))
                {
                    AnsiConsole.MarkupLine($"[red]Error: Test suite file not found: {_args.TestSuite}[/]");
                    return false;
                }

                var json = File.ReadAllText(_args.TestSuite);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var externalData = JsonSerializer.Deserialize<ExternalTestSuiteJson>(json, options);

                if (externalData == null)
                {
                    AnsiConsole.MarkupLine("[red]Error: Failed to parse test suite JSON file[/]");
                    return false;
                }

                if (string.IsNullOrEmpty(externalData.Name))
                {
                    AnsiConsole.MarkupLine("[red]Error: Test suite JSON must have a 'name' property[/]");
                    return false;
                }

                if (externalData.Categories == null || externalData.Categories.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: Test suite JSON must have at least one category[/]");
                    return false;
                }

                _testSuite = new ExternalTestSuite(externalData);
                PrintTestSuiteInfo(isExternal: true);
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error loading test suite: {ex.Message}[/]");
                return false;
            }
        }

        /// <summary>
        /// Print test suite information
        /// </summary>
        private void PrintTestSuiteInfo(bool isExternal = false)
        {
            var prefix = isExternal ? "Using external test suite" : "Using test suite";
            var numPredictInfo = _testSuite.NumPredict != 4096 ? $", max tokens: {_testSuite.NumPredict}" : "";
            AnsiConsole.MarkupLine($"[dim]{prefix}: {_testSuite.Name} ({_testSuite.TotalQuestions} questions{numPredictInfo})[/]");
        }

        /// <summary>
        /// Initialize or load existing results file
        /// </summary>
        private async Task<bool> InitializeResultsFileAsync()
        {
            var filePath = GetOutputFilePath();

            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    _resultsFile = JsonSerializer.Deserialize<QcResultsFile>(json)
                        ?? throw new InvalidDataException("Failed to deserialize results file");

                    // Ensure Results list is initialized (may be null from JSON)
                    _resultsFile.Results ??= new List<QuantResult>();

                    // Validate compatibility
                    if (_resultsFile.ModelName != _args.ModelName)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Results file is for different model ({_resultsFile.ModelName})[/]");
                        return false;
                    }

                    if (_resultsFile.TestSuiteName != _testSuite.Name)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Results file uses different test suite ({_resultsFile.TestSuiteName})[/]");
                        return false;
                    }

                    AnsiConsole.MarkupLine($"[dim]Loaded existing results file ({_resultsFile.Results.Count} quantizations tested)[/]");

                    // Repair IsBase flag if needed (for files created before this fix)
                    RepairBaseFlag();

                    return true;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error loading results file: {ex.Message}[/]");
                    return false;
                }
            }

            // Create new results file
            _resultsFile = new QcResultsFile
            {
                TestSuiteName = _testSuite.Name,
                ModelName = _args.ModelName,
                Options = new QcTestOptions
                {
                    Temperature = _args.Temperature,
                    Seed = _args.Seed,
                    TopP = _args.TopP,
                    TopK = _args.TopK,
                    RepeatPenalty = _args.RepeatPenalty,
                    FrequencyPenalty = _args.FrequencyPenalty
                }
            };

            AnsiConsole.MarkupLine($"[dim]Creating new results file[/]");
            return true;
        }

        /// <summary>
        /// Get output file path
        /// </summary>
        private string GetOutputFilePath()
        {
            if (!string.IsNullOrEmpty(_args.OutputFile))
                return _args.OutputFile;

            // Sanitize model name for filename - replace / and \ with -
            var sanitizedName = _args.ModelName.Replace('/', '-').Replace('\\', '-');
            return $"{sanitizedName}.qc.json";
        }

        /// <summary>
        /// Save results file to disk
        /// </summary>
        private void SaveResultsFile()
        {
            var filePath = GetOutputFilePath();
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_resultsFile, options);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Get model information from Ollama API
        /// </summary>
        private async Task<ModelMetadata?> GetModelInfoAsync(string modelName)
        {
            try
            {
                // First, get model details from /api/show
                var showRequest = new { name = modelName };
                var showResponse = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/show", showRequest);

                if (!showResponse.IsSuccessStatusCode)
                    return null;

                var showJson = await showResponse.Content.ReadAsStringAsync();
                using var showDoc = JsonDocument.Parse(showJson);
                var showRoot = showDoc.RootElement;

                // Parse model details
                var details = showRoot.GetProperty("details");
                var family = details.GetProperty("family").GetString() ?? "";
                var parameterSize = details.GetProperty("parameter_size").GetString() ?? "";
                var quantizationType = details.GetProperty("quantization_level").GetString() ?? "";

                // Get model size from /api/tags endpoint
                var tagsResponse = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                if (!tagsResponse.IsSuccessStatusCode)
                    return null;

                var tagsJson = await tagsResponse.Content.ReadAsStringAsync();
                using var tagsDoc = JsonDocument.Parse(tagsJson);
                var modelsArray = tagsDoc.RootElement.GetProperty("models");

                long sizeBytes = 0;
                foreach (var model in modelsArray.EnumerateArray())
                {
                    var name = model.GetProperty("name").GetString();
                    if (name?.ToLowerInvariant() == modelName.ToLowerInvariant())
                    {
                        sizeBytes = model.GetProperty("size").GetInt64();
                        break;
                    }
                }

                return new ModelMetadata
                {
                    Family = family,
                    ParameterSize = parameterSize,
                    QuantizationType = quantizationType,
                    SizeBytes = sizeBytes
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Preload model into memory using empty chat request
        /// </summary>
        private async Task<bool> PreloadModelAsync(string modelName)
        {
            try
            {
                var request = new
                {
                    model = modelName,
                    messages = new[] { new { role = "user", content = "Hi" } },
                    stream = false
                };

                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Run complete test suite on a quantization
        /// </summary>
        private async Task<QuantResult?> RunTestSuiteAsync(string modelFullName, string tag, ModelMetadata metadata)
        {
            var (result, _) = await RunTestSuiteAsync(modelFullName, tag, metadata, null, null);
            return result;
        }

        /// <summary>
        /// Run complete test suite on a quantization with optional parallel judgment and resume support
        /// Returns tuple of (result, backgroundJudgmentTasks) - caller must await the tasks
        /// </summary>
        private async Task<(QuantResult? result, List<Task> judgmentTasks)> RunTestSuiteAsync(string modelFullName, string tag, ModelMetadata metadata, QuantResult? baseResult, QuantResult? existingPartialResult = null)
        {
            var categories = _testSuite.GetCategories();
            var totalQuestions = _testSuite.TotalQuestions;
            var parallelJudgmentTasks = new List<Task>();
            int judgmentCompletedCount = 0;

            // Start with existing question results if resuming, otherwise empty list
            var questionResults = existingPartialResult?.QuestionResults.ToList() ?? new List<QuestionResult>();

            // Build a set of already-answered question IDs for quick lookup
            var answeredQuestionIds = new HashSet<string>(questionResults.Select(q => q.QuestionId));
            var skippedCount = answeredQuestionIds.Count;

            // Track current partial result for cancellation save
            _currentPartialResult = new QuantResult
            {
                Tag = tag,
                DiskSizeBytes = metadata.SizeBytes,
                Family = metadata.Family,
                ParameterSize = metadata.ParameterSize,
                QuantizationType = metadata.QuantizationType,
                QuestionResults = questionResults
            };

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new ElapsedTimeColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var testTask = ctx.AddTask($"[cyan]Testing {tag}[/]", maxValue: totalQuestions);

                    // If resuming, set initial progress to already-answered count
                    if (skippedCount > 0)
                    {
                        testTask.Increment(skippedCount);
                    }

                    // Add judgment progress bar in parallel mode with base result
                    ProgressTask? judgeTask = null;
                    if (_judgeEnabled && IsParallelMode() && baseResult != null)
                    {
                        judgeTask = ctx.AddTask($"[magenta]Judging {tag}[/]", maxValue: totalQuestions);
                        // Also set initial progress for judgment if resuming
                        if (skippedCount > 0)
                        {
                            // Count how many already have judgments
                            var judgedCount = questionResults.Count(q => q.Judgment != null);
                            judgeTask.Increment(judgedCount);
                        }
                    }

                    foreach (var category in categories)
                    {
                        foreach (var question in category.Questions)
                        {
                            // Check for cancellation
                            if (_cancellationRequested)
                            {
                                testTask.Description = $"[yellow]Cancelled {tag}[/]";
                                _cts.Token.ThrowIfCancellationRequested();
                            }

                            // Skip already-answered questions
                            if (answeredQuestionIds.Contains(question.Id))
                            {
                                continue;
                            }

                            testTask.Description = $"[cyan]{tag}[/] [dim]{category.Name} {question.QuestionId}/{category.Questions.Count}[/]";

                            var result = await RunSingleQuestionAsync(modelFullName, category, question);
                            if (result == null)
                            {
                                // Critical error - cannot continue without logprobs
                                testTask.StopTask();
                                return;
                            }

                            questionResults.Add(result);

                            // In parallel mode, immediately start judging this question (runs in background)
                            if (_judgeEnabled && IsParallelMode() && baseResult != null && judgeTask != null)
                            {
                                var baseQuestion = baseResult.QuestionResults
                                    .FirstOrDefault(q => q.QuestionId == result.QuestionId);

                                if (baseQuestion != null)
                                {
                                    var questionToJudge = result;
                                    var baseToCompare = baseQuestion;
                                    var capturedJudgeTask = judgeTask;

                                    var judgmentTask = Task.Run(async () =>
                                    {
                                        var judgment = await JudgeQuestionAsync(baseToCompare, questionToJudge);
                                        if (judgment != null)
                                        {
                                            questionToJudge.Judgment = judgment;
                                        }
                                        Interlocked.Increment(ref judgmentCompletedCount);
                                        capturedJudgeTask.Increment(1);
                                    });
                                    parallelJudgmentTasks.Add(judgmentTask);
                                }
                            }

                            testTask.Increment(1);
                        }
                    }

                    testTask.Description = $"[green]✓ {tag} testing complete[/]";

                    // Don't wait for judgment here - let it continue in background
                    // But update the description to show it's still running
                    if (judgeTask != null && parallelJudgmentTasks.Count > 0)
                    {
                        judgeTask.Description = $"[magenta]Judging {tag}[/] [dim]({judgmentCompletedCount}/{parallelJudgmentTasks.Count} running in background)[/]";
                    }
                });

            // Clear partial result tracking since we're done
            _currentPartialResult = null;

            if (questionResults.Count == 0)
                return (null, parallelJudgmentTasks);

            var quantResult = new QuantResult
            {
                Tag = tag,
                DiskSizeBytes = metadata.SizeBytes,
                Family = metadata.Family,
                ParameterSize = metadata.ParameterSize,
                QuantizationType = metadata.QuantizationType,
                QuestionResults = questionResults
            };

            return (quantResult, parallelJudgmentTasks);
        }

        /// <summary>
        /// Execute an async operation with retry logic
        /// </summary>
        private async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T?>> operation, string operationName) where T : class
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                try
                {
                    var result = await operation();
                    if (result != null)
                        return result;

                    // Result was null, will retry
                    lastException = null;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                }

                // Show retry message if we have more attempts
                if (attempt < MAX_RETRY_ATTEMPTS)
                {
                    var msg = lastException != null
                        ? $"[yellow]Attempt {attempt + 1}/{MAX_RETRY_ATTEMPTS} for {operationName}: {lastException.Message}[/]"
                        : $"[yellow]Attempt {attempt + 1}/{MAX_RETRY_ATTEMPTS} for {operationName}...[/]";
                    AnsiConsole.MarkupLine(msg);
                    await Task.Delay(RETRY_DELAY_MS * attempt); // Exponential backoff
                }
            }

            // All attempts failed
            if (lastException != null)
            {
                AnsiConsole.MarkupLine($"[red]Failed after {MAX_RETRY_ATTEMPTS} attempts for {operationName}: {lastException.Message}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[red]Failed after {MAX_RETRY_ATTEMPTS} attempts for {operationName}[/]");
            }

            return null;
        }

        /// <summary>
        /// Run a single question and capture logprobs (with retry)
        /// </summary>
        private Task<QuestionResult?> RunSingleQuestionAsync(string modelName, TestCategory category, TestQuestion question)
        {
            return ExecuteWithRetryAsync(
                () => RunSingleQuestionCoreAsync(modelName, category, question),
                $"question {question.Id}");
        }

        /// <summary>
        /// Core implementation for running a single question
        /// </summary>
        private async Task<QuestionResult?> RunSingleQuestionCoreAsync(string modelName, TestCategory category, TestQuestion question)
        {
            // Non-streaming request with logprobs enabled
            var request = new OllamaGenerateRequest
            {
                Model = modelName,
                Prompt = question.Text,
                Stream = false,  // Logprobs only work in non-streaming mode
                Logprobs = true,  // Enable logprobs at root level
                Options = new OllamaGenerateOptions
                {
                    Temperature = _args.Temperature,
                    Seed = _args.Seed,
                    TopP = _args.TopP,
                    TopK = _args.TopK,
                    RepeatPenalty = _args.RepeatPenalty,
                    FrequencyPenalty = _args.FrequencyPenalty,
                    NumPredict = _testSuite.NumPredict
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Check for cancellation before making the request
            _cts.Token.ThrowIfCancellationRequested();

            var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, _cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null; // Will trigger retry
            }

            var responseJson = await response.Content.ReadAsStringAsync(_cts.Token);
            var result = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseJson);

            if (result == null)
            {
                return null; // Will trigger retry
            }

            // Verify logprobs were received
            if (result.Logprobs == null || result.Logprobs.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]Error: No logprobs received for question {question.Id}[/]");
                AnsiConsole.MarkupLine($"[yellow]Logprobs require Ollama v0.12.11 or later. Please update Ollama.[/]");
                // This is a permanent failure, not retryable - throw to fail fast
                throw new InvalidOperationException("Logprobs not available - update Ollama");
            }

            // Extract performance metrics
            double evalTokensPerSecond = 0;
            double promptTokensPerSecond = 0;

            if (result.EvalDuration.HasValue && result.EvalCount.HasValue && result.EvalDuration.Value > 0)
            {
                var evalSeconds = result.EvalDuration.Value / 1_000_000_000.0;
                evalTokensPerSecond = result.EvalCount.Value / evalSeconds;
            }

            if (result.PromptEvalDuration.HasValue && result.PromptEvalCount.HasValue && result.PromptEvalDuration.Value > 0)
            {
                var promptSeconds = result.PromptEvalDuration.Value / 1_000_000_000.0;
                promptTokensPerSecond = result.PromptEvalCount.Value / promptSeconds;
            }

            // Convert logprobs to our format
            var tokens = result.Logprobs.Select(lp => new TokenLogprob
            {
                Token = lp.Token,
                Logprob = lp.Logprob,
                Bytes = lp.Bytes
            }).ToList();

            return new QuestionResult
            {
                QuestionId = question.Id,
                Category = category.Name,
                Question = question.Text,
                Answer = result.Response ?? string.Empty,
                Tokens = tokens,
                EvalTokensPerSecond = evalTokensPerSecond,
                PromptTokensPerSecond = promptTokensPerSecond,
                TotalTokens = result.EvalCount ?? 0
            };
        }

        /// <summary>
        /// Run judgment scoring for a quantization (serial mode)
        /// </summary>
        private async Task RunJudgmentSerialAsync(QuantResult quantResult, QuantResult baseResult)
        {
            if (!_judgeEnabled || _judgeHttpClient == null || _judgeModelName == null)
                return;

            AnsiConsole.MarkupLine($"[cyan]Running judgment for: {quantResult.Tag}[/]");

            var judgedCount = 0;
            var skippedCount = 0;

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new ElapsedTimeColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[magenta]Judging {quantResult.Tag}[/]", maxValue: quantResult.QuestionResults.Count);

                    foreach (var quantQuestion in quantResult.QuestionResults)
                    {
                        // Find corresponding base question
                        var baseQuestion = baseResult.QuestionResults
                            .FirstOrDefault(q => q.QuestionId == quantQuestion.QuestionId);

                        if (baseQuestion == null)
                        {
                            task.Increment(1);
                            continue;
                        }

                        // Check if already judged with same model
                        if (quantQuestion.Judgment != null &&
                            quantQuestion.Judgment.JudgeModel == _judgeModelName &&
                            !_args.Force)
                        {
                            skippedCount++;
                            task.Increment(1);
                            continue;
                        }

                        task.Description = $"[magenta]Judging {quantResult.Tag}[/] [dim]{quantQuestion.Category} Q{quantQuestion.QuestionId}[/]";

                        // Run judgment
                        var judgment = await JudgeQuestionAsync(baseQuestion, quantQuestion);
                        if (judgment != null)
                        {
                            quantQuestion.Judgment = judgment;
                            judgedCount++;
                        }

                        task.Increment(1);
                    }

                    task.Description = $"[green]✓ Judging {quantResult.Tag} complete[/]";
                });

            AnsiConsole.MarkupLine($"[dim]Judged: {judgedCount}, Skipped: {skippedCount}[/]");
            SaveResultsFile();
        }

        /// <summary>
        /// Run judgment scoring for a quantization with per-question parallelism
        /// All questions are judged concurrently with progress tracking
        /// </summary>
        private async Task RunJudgmentParallelAsync(QuantResult quantResult, QuantResult baseResult)
        {
            if (!_judgeEnabled || _judgeHttpClient == null || _judgeModelName == null)
                return;

            var questionsToJudge = new List<(QuestionResult quantQuestion, QuestionResult baseQuestion)>();
            int skippedCount = 0;

            foreach (var quantQuestion in quantResult.QuestionResults)
            {
                // Find corresponding base question
                var baseQuestion = baseResult.QuestionResults
                    .FirstOrDefault(q => q.QuestionId == quantQuestion.QuestionId);

                if (baseQuestion == null)
                {
                    skippedCount++;
                    continue;
                }

                // Check if already judged with same model
                if (quantQuestion.Judgment != null &&
                    quantQuestion.Judgment.JudgeModel == _judgeModelName &&
                    !_args.Force)
                {
                    skippedCount++;
                    continue;
                }

                questionsToJudge.Add((quantQuestion, baseQuestion));
            }

            if (questionsToJudge.Count == 0)
            {
                AnsiConsole.MarkupLine($"[dim]All questions already judged for {quantResult.Tag}[/]");
                return;
            }

            int completedCount = 0;

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new ElapsedTimeColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[magenta]Judging {quantResult.Tag}[/]", maxValue: questionsToJudge.Count);

                    var judgmentTasks = questionsToJudge.Select(pair => Task.Run(async () =>
                    {
                        var judgment = await JudgeQuestionAsync(pair.baseQuestion, pair.quantQuestion);
                        if (judgment != null)
                        {
                            pair.quantQuestion.Judgment = judgment;
                        }
                        Interlocked.Increment(ref completedCount);
                        task.Increment(1);
                    })).ToList();

                    await Task.WhenAll(judgmentTasks);
                    task.Description = $"[green]✓ Judging {quantResult.Tag} complete[/]";
                });

            AnsiConsole.MarkupLine($"[dim]Judged: {questionsToJudge.Count}, Skipped: {skippedCount}[/]");
            SaveResultsFile();
        }

        /// <summary>
        /// Wait for all background judgment tasks with progress tracking
        /// </summary>
        private async Task WaitForBackgroundJudgmentsAsync(ConcurrentDictionary<string, (List<Task> tasks, int totalQuestions)> allTasks)
        {
            if (allTasks.Count == 0)
                return;

            // Check if there are any pending tasks
            var pendingCount = allTasks.Values.Sum(t => t.tasks.Count(task => !task.IsCompleted));
            if (pendingCount == 0)
            {
                AnsiConsole.MarkupLine($"[green]✓ All background judgments already complete[/]");
                return;
            }

            AnsiConsole.MarkupLine($"\n[magenta]Waiting for {pendingCount} background judgment task(s) to complete...[/]");

            await AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new ElapsedTimeColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    // Create a progress task for each quantization's judgment
                    var progressTasks = new Dictionary<string, ProgressTask>();
                    foreach (var kvp in allTasks)
                    {
                        var tag = kvp.Key;
                        var (tasks, total) = kvp.Value;
                        var progressTask = ctx.AddTask($"[magenta]Judging {tag}[/]", maxValue: total);

                        // Count already completed tasks and set initial progress
                        var alreadyCompleted = tasks.Count(t => t.IsCompleted);
                        if (alreadyCompleted > 0)
                        {
                            progressTask.Increment(alreadyCompleted);
                        }

                        // Track completion of remaining tasks
                        foreach (var task in tasks.Where(t => !t.IsCompleted))
                        {
                            _ = task.ContinueWith(_ => progressTask.Increment(1), TaskContinuationOptions.ExecuteSynchronously);
                        }
                        progressTasks[tag] = progressTask;
                    }

                    // Wait for all tasks from all quantizations
                    var allTasksList = allTasks.Values.SelectMany(t => t.tasks).ToList();
                    await Task.WhenAll(allTasksList);

                    // Mark all progress tasks as complete
                    foreach (var kvp in progressTasks)
                    {
                        kvp.Value.Description = $"[green]✓ Judging {kvp.Key} complete[/]";
                    }
                });

            AnsiConsole.MarkupLine($"[green]✓ All background judgments complete[/]");
        }

        /// <summary>
        /// Judge a single question comparison (with retry)
        /// </summary>
        private Task<JudgmentResult?> JudgeQuestionAsync(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (_judgeHttpClient == null || _judgeModelName == null || _judgeBaseUrl == null)
                return Task.FromResult<JudgmentResult?>(null);

            return ExecuteWithRetryAsync(
                () => JudgeQuestionCoreAsync(baseQuestion, quantQuestion),
                $"judgment Q{baseQuestion.QuestionId}");
        }

        /// <summary>
        /// Core implementation for judging a single question
        /// </summary>
        private async Task<JudgmentResult?> JudgeQuestionCoreAsync(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (_judgeHttpClient == null || _judgeModelName == null || _judgeBaseUrl == null)
                return null;

            // Build the judgment prompt
            var userMessage = $@"Question: {baseQuestion.Question}

Base answer:
{baseQuestion.Answer}

Quantized answer:
{quantQuestion.Answer}

Evaluate the similarity between these two answers.";

            // Use /api/chat with structured output schema
            var request = new
            {
                model = _judgeModelName,
                messages = new[]
                {
                    new { role = "system", content = JUDGE_SYSTEM_PROMPT },
                    new { role = "user", content = userMessage }
                },
                stream = false,
                format = new
                {
                    type = "object",
                    properties = new
                    {
                        score = new
                        {
                            type = "integer",
                            description = "Similarity score from 1 to 100"
                        },
                        reason = new
                        {
                            type = "string",
                            description = "Brief explanation for the score"
                        }
                    },
                    required = new[] { "score", "reason" }
                },
                options = new
                {
                    temperature = 0.0,
                    seed = 42,
                    num_predict = 200
                }
            };

            var jsonContent = JsonSerializer.Serialize(request);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // Check for cancellation before making the request
            _cts.Token.ThrowIfCancellationRequested();

            var response = await _judgeHttpClient.PostAsync($"{_judgeBaseUrl}/api/chat", content, _cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(_cts.Token);
                throw new HttpRequestException($"HTTP {(int)response.StatusCode}: {errorBody}");
            }

            var responseJson = await response.Content.ReadAsStringAsync(_cts.Token);
            using var doc = JsonDocument.Parse(responseJson);
            var root = doc.RootElement;

            // Chat API returns message.content instead of response
            if (!root.TryGetProperty("message", out var messageElement))
            {
                throw new InvalidOperationException($"No 'message' field in API response: {responseJson}");
            }

            if (!messageElement.TryGetProperty("content", out var contentElement))
            {
                throw new InvalidOperationException($"No 'content' field in message: {responseJson}");
            }

            var responseText = contentElement.GetString() ?? "";

            // Parse the structured JSON response
            int score = 0;
            string reason = "";

            try
            {
                using var scoreDoc = JsonDocument.Parse(responseText);
                var scoreRoot = scoreDoc.RootElement;

                // Try "score" field first, then "similarity" as fallback (some models use this)
                if (scoreRoot.TryGetProperty("score", out var scoreElement))
                {
                    if (scoreElement.ValueKind == JsonValueKind.Number)
                    {
                        score = (int)Math.Round(scoreElement.GetDouble());
                    }
                }
                else if (scoreRoot.TryGetProperty("similarity", out var simElement))
                {
                    if (simElement.ValueKind == JsonValueKind.Number)
                    {
                        // Handle similarity as 0-1 range or 0-100 range
                        var simValue = simElement.GetDouble();
                        score = simValue <= 1.0 ? (int)Math.Round(simValue * 100) : (int)Math.Round(simValue);
                    }
                }

                // Try "reason" field first, then "response" as fallback
                if (scoreRoot.TryGetProperty("reason", out var reasonElement))
                {
                    reason = reasonElement.GetString() ?? "";
                }
                else if (scoreRoot.TryGetProperty("response", out var respElement))
                {
                    reason = respElement.GetString() ?? "";
                }
            }
            catch
            {
                // Try regex fallback for malformed JSON - check both score and similarity
                var scoreMatch = Regex.Match(responseText, @"""?(?:score|similarity)""?\s*:\s*(\d+(?:\.\d+)?)");
                if (scoreMatch.Success)
                {
                    if (double.TryParse(scoreMatch.Groups[1].Value, out var parsed))
                    {
                        score = parsed <= 1.0 ? (int)Math.Round(parsed * 100) : (int)Math.Round(parsed);
                    }
                }

                var reasonMatch = Regex.Match(responseText, @"""?(?:reason|response)""?\s*:\s*""([^""]+)""");
                if (reasonMatch.Success)
                {
                    reason = reasonMatch.Groups[1].Value;
                }
            }

            // If score is 0 or negative, treat as minimum score (1) - some models ignore instructions
            if (score <= 0)
            {
                score = 1;
            }

            // Validate score range
            score = Math.Clamp(score, 1, 100);

            return new JudgmentResult
            {
                JudgeModel = _judgeModelName,
                Score = score,
                Reason = reason,
                JudgedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Verify judge model exists on the server (doesn't preload, just checks existence)
        /// </summary>
        private async Task<bool> VerifyJudgeModelExistsAsync()
        {
            if (_judgeHttpClient == null || _judgeModelName == null || _judgeBaseUrl == null)
                return false;

            try
            {
                // Use /api/show to check if model exists
                var request = new { name = _judgeModelName };
                var response = await _judgeHttpClient.PostAsJsonAsync($"{_judgeBaseUrl}/api/show", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Helper class for model metadata
        /// </summary>
        private class ModelMetadata
        {
            public string Family { get; set; } = string.Empty;
            public string ParameterSize { get; set; } = string.Empty;
            public string QuantizationType { get; set; } = string.Empty;
            public long SizeBytes { get; set; }
        }
    }
}
