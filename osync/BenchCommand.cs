using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace osync
{
    /// <summary>
    /// Exception thrown when a fatal error occurs during benchmarking that should abort the entire test.
    /// </summary>
    public class BenchFatalErrorException : Exception
    {
        public BenchFatalErrorException(string message) : base(message) { }
        public BenchFatalErrorException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Custom progress column that displays task-specific suffix text (timing stats and model name)
    /// </summary>
    public class ProgressSuffixColumn : ProgressColumn
    {
        private readonly ConcurrentDictionary<int, string> _taskSuffixes = new();

        /// <summary>
        /// Default suffix used when a task doesn't have a specific suffix set
        /// </summary>
        public string DefaultSuffix { get; set; } = "";

        /// <summary>
        /// Set suffix for a specific task
        /// </summary>
        public void SetSuffix(ProgressTask task, string suffix)
        {
            _taskSuffixes[task.Id] = suffix;
        }

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var suffix = _taskSuffixes.TryGetValue(task.Id, out var s) ? s : DefaultSuffix;
            return new Markup(suffix);
        }
    }

    /// <summary>
    /// Implementation of the bench command for context length benchmarking.
    /// Supports ctxbench (no tools) and ctxtoolsbench (with tools) test types.
    /// </summary>
    public class BenchCommand
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly BenchArgs _args;
        private BenchTestSuite _testSuite = null!;
        private BenchResultsFile _resultsFile = null!;

        // Judge model fields
        private HttpClient? _judgeHttpClient;
        private string? _judgeBaseUrl;
        private string? _judgeModelName;
        private bool _judgeEnabled;
        private string? _testOllamaVersion;
        private string? _judgeOllamaVersion;
        private ICloudJudgeProvider? _cloudJudgeProvider;

        // Cancellation support
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private volatile bool _cancellationRequested;
        private BenchQuantResult? _currentPartialResult;
        private bool _currentModelPulledOnDemand;
        private readonly HashSet<string> _modelsPulledThisSession = new HashSet<string>();

        // Retry configuration
        private const int MAX_RETRY_ATTEMPTS = 5;
        private const int RETRY_DELAY_MS = 1000;
        private const int JUDGE_MAX_RETRY_ATTEMPTS = 25;
        private const int JUDGE_RETRY_DELAY_START_MS = 5000;
        private const int JUDGE_RETRY_DELAY_END_MS = 30000;

        /// <summary>
        /// Format milliseconds to human-readable duration string
        /// e.g., 96ms, 1.2s, 7m32s
        /// </summary>
        private static string FormatDurationMs(long milliseconds)
        {
            if (milliseconds < 1000)
                return $"{milliseconds}ms";
            if (milliseconds < 60000)
                return $"{milliseconds / 1000.0:F1}s";
            var minutes = milliseconds / 60000;
            var seconds = (milliseconds % 60000) / 1000;
            return $"{minutes}m{seconds}s";
        }

        // Context tracking
        private ContextTracker? _contextTracker;

        // Progress tracking for overall test suite
        private int _totalSuiteQuestions;
        private int _completedSuiteQuestions;
        private string _currentModelTag = "";

        // Effective max context length (may be limited by -L argument)
        private int _effectiveMaxContext;

        // Model's reported max context length from API
        private int _modelMaxContextLength;

        // Whether thinking mode is detected for the model
        private bool _thinkingEnabled;

        // Effective overhead based on thinking detection
        private int _effectiveOverhead;

        // Log file writer for process logging
        private LogFileWriter? _logger;

        // Calibration tracking
        private CalibrationData? _calibrationData;
        private CalibrationCategory? _currentCalibrationCategory;
        private int _cumulativeEstimatedTokens;
        private int _cumulativeCharCount;

        // Parallel judgment support
        private ConcurrentQueue<JudgmentQueueItem>? _judgmentQueue;
        private Task? _judgmentConsumerTask;
        private volatile int _judgmentQueuedCount;
        private volatile int _judgmentCompletedCount;
        private volatile bool _judgmentQueueComplete;
        private ConcurrentQueue<CompletedJudgment>? _completedJudgments; // For verbose output
        private ProgressTask? _judgmentProgressTask; // For progress bar

        // Default judge system prompt for YES/NO evaluation
        private const string JUDGE_SYSTEM_PROMPT = @"You are an impartial judge evaluating answer correctness.
Your task is to determine if a model's answer matches the reference answer.
Be lenient with minor wording differences but strict with factual accuracy.
Always respond in valid JSON format: {""Answer"": ""YES"" or ""NO"", ""Reason"": ""explanation""}";

        public BenchCommand(BenchArgs args, string baseUrl = "http://localhost:11434")
        {
            _args = args;
            _baseUrl = baseUrl.TrimEnd('/');

            if (_args.Timeout <= 0)
                _args.Timeout = 1800;

            // Use infinite timeout on HttpClient - we control timeout via per-request CancellationTokenSource
            // This allows _args.Timeout to be modified dynamically without HttpClient error
            _httpClient = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            // Initialize judge if specified
            if (!string.IsNullOrEmpty(_args.Judge))
            {
                ParseJudgeArgument(_args.Judge);
            }
        }

        /// <summary>
        /// Parse the --judge argument to extract provider/model
        /// Supports flexible URL formats like copy command:
        /// - modelname → local model on localhost
        /// - http://server:port/model → full URL
        /// - 192.168.100.100/model → IP address detected as server
        /// - myserver:11434/model → hostname with port
        /// - myserver//model → trailing slash indicates server
        /// </summary>
        private void ParseJudgeArgument(string judgeArg)
        {
            if (judgeArg.StartsWith("@"))
            {
                // Cloud provider syntax: @provider[:key]/model
                var config = CloudJudgeProviderFactory.ParseArgument(judgeArg);
                if (config != null)
                {
                    _cloudJudgeProvider = CloudJudgeProviderFactory.CreateProvider(config, _args.Timeout);
                    _judgeModelName = _cloudJudgeProvider?.ModelName;
                    _judgeEnabled = _cloudJudgeProvider != null;
                }
                return;
            }

            // Check if it's a remote server with model
            string? serverPart = null;
            string modelPart = judgeArg;

            if (judgeArg.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                judgeArg.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Parse as full URL: http://host:port/model
                var uri = new Uri(judgeArg);
                serverPart = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                modelPart = uri.AbsolutePath.TrimStart('/');
            }
            else
            {
                // Check for IP or hostname:port before model name
                // Split by first `/` and check if first part looks like a server
                var slashIndex = judgeArg.IndexOf('/');
                if (slashIndex > 0)
                {
                    var possibleServer = judgeArg.Substring(0, slashIndex);
                    // Check if it looks like a remote server (IP, hostname:port, or with trailing /)
                    if (OsyncProgram.LooksLikeRemoteServer(possibleServer) ||
                        OsyncProgram.LooksLikeRemoteServer(possibleServer + "/"))
                    {
                        serverPart = OsyncProgram.NormalizeServerUrl(possibleServer);
                        modelPart = judgeArg.Substring(slashIndex + 1).TrimStart('/');
                    }
                }
            }

            if (serverPart != null)
            {
                // Remote Ollama server
                _judgeBaseUrl = serverPart;
                _judgeModelName = modelPart;
                _judgeHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                _judgeEnabled = true;
            }
            else
            {
                // Local Ollama model - always uses localhost regardless of -d destination
                _judgeBaseUrl = "http://localhost:11434";
                _judgeModelName = judgeArg;
                _judgeHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
                _judgeEnabled = true;
            }
        }

        /// <summary>
        /// Helper method to write to both console (with markup) and log file (plain text).
        /// </summary>
        private void Log(string markup)
        {
            AnsiConsole.MarkupLine(markup);
            _logger?.WriteLine(markup);
        }

        /// <summary>
        /// Check if running in parallel judgment mode
        /// </summary>
        private bool IsParallelMode()
        {
            return _args.JudgeMode?.Equals("parallel", StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Item in the parallel judgment queue
        /// </summary>
        private class JudgmentQueueItem
        {
            public BenchQuestionResult Result { get; set; } = null!;
            public BenchQuestion Question { get; set; } = null!;
            public BenchCategory Category { get; set; } = null!;
            public BenchSubCategory? SubCategory { get; set; }
            public BenchQuantResult QuantResult { get; set; } = null!;
        }

        /// <summary>
        /// Completed judgment for verbose output logging
        /// </summary>
        private class CompletedJudgment
        {
            public int QuestionId { get; set; }
            public string Category { get; set; } = "";
            public string? SubCategory { get; set; }
            public string Answer { get; set; } = "";
            public string Reason { get; set; } = "";
            public bool IsCorrect { get; set; }
            // For serial-style output format
            public int SequentialNumber { get; set; } // Q#/Total (sequential across all questions)
            public int SubCatQuestionNumber { get; set; } // Question position within subcategory
            public int SubCatTotal { get; set; } // Total questions in subcategory
            public int CategoryTotal { get; set; } // Total questions in category
        }

        /// <summary>
        /// Result from JudgeAnswerAsync including judgment and performance metrics
        /// </summary>
        private class JudgeResult
        {
            public BenchJudgment? Judgment { get; set; }
            public int PromptTokens { get; set; }
            public int EvalTokens { get; set; }
            public double PromptDurationMs { get; set; }
            public double EvalDurationMs { get; set; }
            public double PromptToksPerSec => PromptDurationMs > 0 ? PromptTokens / (PromptDurationMs / 1000.0) : 0;
            public double EvalToksPerSec => EvalDurationMs > 0 ? EvalTokens / (EvalDurationMs / 1000.0) : 0;
        }

        // Tracking for parallel judgment output format
        private int _parallelJudgmentSequence;
        private Dictionary<string, (int current, int total)>? _parallelSubCatCounts;
        private Dictionary<string, int>? _parallelCategoryCounts;

        // Judgment metrics tracking
        private List<double>? _judgmentTimings; // Response times in seconds
        private List<double>? _judgmentPromptSpeeds; // Prompt tok/s
        private List<double>? _judgmentEvalSpeeds; // Eval tok/s
        private string? _judgmentSuffixInfo; // Pre-computed suffix for progress bar

        /// <summary>
        /// Main execution method
        /// </summary>
        public async Task<int> ExecuteAsync()
        {
            // Initialize log file writer if --logfile is specified
            if (!string.IsNullOrWhiteSpace(_args.LogFile))
            {
                _logger = new LogFileWriter(_args.LogFile);
                if (_logger.IsEnabled)
                {
                    Log($"[dim]Logging to: {_logger.FilePath}[/]");
                }
            }

            try
            {
                return await ExecuteInternalAsync();
            }
            finally
            {
                _logger?.Dispose();
            }
        }

        /// <summary>
        /// Internal execution method (wrapped for logger disposal)
        /// </summary>
        private async Task<int> ExecuteInternalAsync()
        {
            // Handle special flags
            if (_args.HelpCloud)
            {
                PrintCloudProviderHelp();
                return 0;
            }

            if (_args.ShowTools)
            {
                PrintToolsCheatSheet();
                return 0;
            }

            if (_args.GenerateSuite)
            {
                return GenerateTestSuites();
            }

            // Handle --fix option
            if (_args.Fix)
            {
                return await FixResultsFileAsync();
            }

            // Validate required arguments
            if (string.IsNullOrWhiteSpace(_args.ModelName))
            {
                Log("[red]Error: ModelName (-M) is required[/]");
                Log("[dim]Use --help-cloud for cloud provider documentation[/]");
                Log("[dim]Use --showtools to view available tools[/]");
                Log("[dim]Use --generate-suite to create default test suites[/]");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(_args.Quants))
            {
                Log("[red]Error: Quants (-Q) is required[/]");
                return 1;
            }

            if (string.IsNullOrWhiteSpace(_args.TestSuite))
            {
                Log("[red]Error: TestSuite (-T) is required[/]");
                Log("[dim]Use --generate-suite to create default test suites[/]");
                return 1;
            }

            // Set up Ctrl+C handler
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                // Load test suite
                if (!LoadTestSuite())
                    return 1;

                // Validate TestType supports judgment
                if (_testSuite.JudgeRequired && !_judgeEnabled)
                {
                    Log($"[yellow]Warning: TestType '{_testSuite.TestType}' requires a judge model. Use --judge to specify one.[/]");
                }

                // Verify test server connection FIRST (before any other network operations)
                var (testServerConnected, testServerVersion, testServerError) = await VerifyServerConnectionAsync(_httpClient, _baseUrl);
                if (!testServerConnected)
                {
                    Log($"[red]Error: Cannot connect to test server at {_baseUrl}[/]");
                    Log($"[red]{testServerError}[/]");
                    return 1;
                }

                // Print test server info
                var isRemoteTestServer = !_baseUrl.Contains("localhost") && !_baseUrl.Contains("127.0.0.1");
                if (isRemoteTestServer)
                {
                    Log($"[cyan]Test server: {_baseUrl} (Ollama {testServerVersion ?? "unknown"})[/]");
                }
                else
                {
                    Log($"[dim]Test server: {_baseUrl} (Ollama {testServerVersion ?? "unknown"})[/]");
                }

                // Initialize or load results file
                if (!await InitializeResultsFileAsync())
                    return 1;

                // Print output file path
                Log($"[dim]Output file: {GetOutputFilePath()}[/]");
                Log($"[dim]Test type: {_testSuite.TestType} - {_testSuite.TestDescription}[/]");

                if (_testSuite.ToolsEnabled)
                {
                    Log($"[cyan]Tools enabled: {(_testSuite.EnabledTools?.Count > 0 ? string.Join(", ", _testSuite.EnabledTools) : "all")}[/]");
                }

                if (_args.OnDemand)
                {
                    Log($"[cyan]On-demand mode: Models will be pulled if missing and removed after testing[/]");
                }

                // Parse quantization tags
                var rawQuantTags = _args.Quants.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(q => q.Trim())
                    .ToList();

                if (rawQuantTags.Count == 0)
                {
                    Log("[red]Error: No quantization tags specified[/]");
                    return 1;
                }

                // Expand wildcards
                var quantTags = await ExpandWildcardTagsAsync(rawQuantTags);

                if (quantTags.Count == 0)
                {
                    Log("[red]Error: No quantization tags found after wildcard expansion[/]");
                    return 1;
                }

                // Verify judge model
                if (_judgeEnabled)
                {
                    Log($"[dim]Verifying judge model/connection...[/]");

                    if (_cloudJudgeProvider != null)
                    {
                        var (success, errorMessage) = await _cloudJudgeProvider.ValidateConnectionAsync();
                        if (!success)
                        {
                            Log($"[red]Error: {errorMessage}[/]");
                            return 1;
                        }
                        Log($"[dim]Cloud judge verified: {_judgeModelName} @ {_cloudJudgeProvider.ProviderName}[/]");
                    }
                    else if (_judgeHttpClient != null)
                    {
                        // First verify server connection
                        var (judgeConnected, judgeOllamaVersion, judgeError) = await VerifyServerConnectionAsync(_judgeHttpClient, _judgeBaseUrl!);
                        if (!judgeConnected)
                        {
                            Log($"[red]Error: Cannot connect to judge server at {_judgeBaseUrl}[/]");
                            Log($"[red]{judgeError}[/]");
                            return 1;
                        }

                        // Then check if the model exists
                        if (!await VerifyJudgeModelExistsAsync())
                        {
                            Log($"[red]Error: Judge model '{_judgeModelName}' not found on server {_judgeBaseUrl}[/]");
                            return 1;
                        }

                        // Display server info
                        var isRemoteJudge = !_judgeBaseUrl!.Contains("localhost") && !_judgeBaseUrl.Contains("127.0.0.1");
                        if (isRemoteJudge)
                        {
                            Log($"[cyan]Judge server: {_judgeBaseUrl} (Ollama {judgeOllamaVersion ?? "unknown"})[/]");
                            Log($"[dim]Judge model verified: {_judgeModelName}[/]");
                        }
                        else
                        {
                            Log($"[dim]Judge model verified: {_judgeModelName} (Ollama {judgeOllamaVersion ?? "unknown"})[/]");
                        }
                    }
                }

                // Check if any models will actually need testing (vs all being skipped for rejudge)
                bool anyModelsNeedTesting = false;

                // Determine which categories will be requested (applying -L limit if specified)
                HashSet<string> requestedCategories;
                if (!string.IsNullOrWhiteSpace(_args.Limit))
                {
                    // -L specified: include categories up to and including the limit
                    var limitCategory = _testSuite.Categories.FirstOrDefault(c =>
                        c.Name.Equals(_args.Limit, StringComparison.OrdinalIgnoreCase));
                    if (limitCategory != null)
                    {
                        var limitContext = limitCategory.ContextLength;
                        requestedCategories = _testSuite.Categories
                            .Where(c => c.ContextLength <= limitContext)
                            .Select(c => c.Name)
                            .ToHashSet();
                    }
                    else
                    {
                        // Invalid limit - will be caught later, assume all categories for now
                        requestedCategories = _testSuite.Categories.Select(c => c.Name).ToHashSet();
                    }
                }
                else
                {
                    // No limit - all categories
                    requestedCategories = _testSuite.Categories.Select(c => c.Name).ToHashSet();
                }

                foreach (var quantTag in quantTags)
                {
                    var existingResult = _resultsFile.Results.FirstOrDefault(r =>
                        r.Tag.Equals(quantTag, StringComparison.OrdinalIgnoreCase));

                    if (existingResult == null || !existingResult.IsComplete || _args.Force)
                    {
                        anyModelsNeedTesting = true;
                        break;
                    }

                    var testedCategories = existingResult.CategoryResults?.Select(c => c.Category).ToHashSet() ?? new HashSet<string>();
                    if (!testedCategories.SetEquals(requestedCategories))
                    {
                        anyModelsNeedTesting = true;
                        break;
                    }
                    // If we get here, this model would be skipped (already tested with same categories)
                    // Continue checking other models
                }

                // Only run pre-flight checks if we'll actually be testing
                if (anyModelsNeedTesting)
                {
                    // Ensure first model exists for context length resolution
                    var firstModel = quantTags[0];
                    var firstModelExists = await CheckModelExistsAsync(firstModel);

                    if (!firstModelExists)
                    {
                        if (_args.OnDemand)
                        {
                            Log($"[cyan]Pulling first model for pre-flight checks: {firstModel}[/]");
                            if (!await PullModelAsync(firstModel))
                            {
                                Log($"[red]Failed to pull model: {firstModel}[/]");
                                return 1;
                            }
                            _modelsPulledThisSession.Add(firstModel);
                        }
                        else
                        {
                            Log($"[red]Error: First model '{firstModel}' not found. Use --ondemand to pull automatically.[/]");
                            return 1;
                        }
                    }

                    // Resolve context length settings (model max, thinking detection, overhead)
                    if (!await ResolveContextLengthSettingsAsync(firstModel))
                        return 1;
                }

                // Calculate total questions for progress tracking (after -L limit is applied)
                _totalSuiteQuestions = 0;
                foreach (var cat in _testSuite.Categories)
                {
                    if (cat.Questions != null)
                        _totalSuiteQuestions += cat.Questions.Count;
                    if (cat.SubCategories != null)
                        _totalSuiteQuestions += cat.SubCategories.Sum(s => s.Questions?.Count ?? 0);
                }

                AnsiConsole.WriteLine();

                // Note: Model unloading is now handled per-model in ProcessQuantTagAsync
                // with smart logic to skip unloading if same model is already loaded

                // Initialize calibration tracking if --calibrate is enabled
                if (_args.Calibrate)
                {
                    _calibrationData = new CalibrationData
                    {
                        ModelName = $"{_args.ModelName}:{_args.Quants}",
                        CalibratedAt = DateTime.UtcNow,
                        OsyncVersion = OsyncProgram.GetFullVersion(),
                        CharsPerTokenRatio = BenchTokenizer.DefaultCharsPerToken
                    };
                    Log($"[cyan]Calibration mode enabled - tracking estimated vs actual tokens[/]");
                    Log($"[dim]Using chars/token ratio: {BenchTokenizer.DefaultCharsPerToken}[/]");
                }

                // Process each quantization tag
                foreach (var quantTag in quantTags)
                {
                    if (_cancellationRequested) break;
                    await ProcessQuantTagAsync(quantTag);
                }

                // Save final results
                await SaveResultsFileAsync();

                // Save calibration data if enabled
                if (_args.Calibrate && _calibrationData != null)
                {
                    await SaveCalibrationDataAsync();
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log($"[red]Error: {ex.Message}[/]");
                await SaveResultsFileAsync();
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= OnCancelKeyPress;
            }
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _cancellationRequested = true;
            _cts.Cancel();
            Log("\n[yellow]Cancellation requested. Saving progress...[/]");
        }

        #region Test Suite Loading

        // Store the resolved test suite file path
        private string _testSuiteFilePath = string.Empty;

        // Store the computed test suite digest
        private string _testSuiteDigest = string.Empty;

        /// <summary>
        /// Compute SHA256 digest of a file's content.
        /// </summary>
        private static string ComputeFileDigest(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private bool LoadTestSuite()
        {
            try
            {
                // Resolve test suite file path from -T argument
                // Priority: 1) Try as filename, 2) If not found and is test type, resolve to default
                string filePath = _args.TestSuite;

                // First, try as a direct file path
                if (File.Exists(filePath))
                {
                    // File exists, use it directly
                    _testSuiteFilePath = filePath;
                }
                else
                {
                    // File doesn't exist - check if it's a test type that can be resolved
                    var testType = _args.TestSuite.Trim().ToLowerInvariant();
                    if (testType == "ctxbench" || testType == "ctxtoolsbench")
                    {
                        // Resolve test type to default filename
                        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), $"v1{testType}.json");
                        if (File.Exists(defaultPath))
                        {
                            filePath = defaultPath;
                            _testSuiteFilePath = filePath;
                            Log($"[dim]Resolved '{_args.TestSuite}' to: {filePath}[/]");
                        }
                        else
                        {
                            Log($"[red]Error: Test suite file not found: {_args.TestSuite}[/]");
                            Log($"[dim]Also tried default: {defaultPath}[/]");
                            Log($"[dim]Run 'osync bench --generate-suite -T {testType}' to create it.[/]");
                            return false;
                        }
                    }
                    else
                    {
                        Log($"[red]Error: Test suite file not found: {filePath}[/]");
                        return false;
                    }
                }

                Log($"[dim]Loading test suite: {_testSuiteFilePath}[/]");

                // Compute SHA256 digest of the test suite file for validation
                _testSuiteDigest = ComputeFileDigest(filePath);

                var json = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _testSuite = JsonSerializer.Deserialize<BenchTestSuite>(json, options)!;

                if (string.IsNullOrEmpty(_testSuite.TestType))
                {
                    Log($"[red]Error: Test suite missing TestType field[/]");
                    return false;
                }

                // Note: Category limits and context length resolution are now handled in
                // ResolveContextLengthSettingsAsync() which runs after model verification

                return true;
            }
            catch (Exception ex)
            {
                Log($"[red]Error loading test suite: {ex.Message}[/]");
                return false;
            }
        }

        #endregion

        #region Results File Management

        private string GetOutputFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_args.OutputFile))
                return _args.OutputFile;

            return $"{_args.ModelName}.{_testSuite.TestType}.json";
        }

        private async Task<bool> InitializeResultsFileAsync()
        {
            var outputPath = GetOutputFilePath();

            if (File.Exists(outputPath) && !_args.Force)
            {
                try
                {
                    // Use streaming deserialization to handle large files without OOM
                    await using var fileStream = File.OpenRead(outputPath);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _resultsFile = (await JsonSerializer.DeserializeAsync<BenchResultsFile>(fileStream, options))!;

                    if (_resultsFile.TestType != _testSuite.TestType)
                    {
                        Log($"[yellow]Warning: Results file has different TestType ({_resultsFile.TestType} vs {_testSuite.TestType})[/]");
                    }

                    // Validate test suite digest - ensures results are compatible
                    if (!string.IsNullOrEmpty(_resultsFile.TestSuiteDigest) &&
                        _resultsFile.TestSuiteDigest != _testSuiteDigest)
                    {
                        Log($"[red]Error: Test suite mismatch![/]");
                        Log($"[red]  Results file was created with a different test suite version.[/]");
                        Log($"[red]  Results digest: {_resultsFile.TestSuiteDigest}[/]");
                        Log($"[red]  Current digest: {_testSuiteDigest}[/]");
                        Log($"[yellow]Use --force to override or use the original test suite file.[/]");
                        return false;
                    }

                    // Validate that -M model matches the results file
                    if (!string.IsNullOrEmpty(_resultsFile.ModelName) &&
                        !_resultsFile.ModelName.Equals(_args.ModelName, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"[red]Error: Model name mismatch![/]");
                        Log($"[red]  Results file contains: {_resultsFile.ModelName}[/]");
                        Log($"[red]  Command line specifies: {_args.ModelName}[/]");
                        Log($"[yellow]Use --force to override or specify the correct model with -M[/]");
                        if (!_args.Force)
                            return false;
                        Log($"[yellow]--force specified, continuing with mismatched model name[/]");
                    }

                    Log($"[dim]Resuming from existing results file with {_resultsFile.Results.Count} model(s)[/]");

                    // Create backup of existing results before continuing
                    BackupHelper.CreateBackup(outputPath, Log);

                    return true;
                }
                catch (Exception ex)
                {
                    Log($"[yellow]Warning: Could not load existing results file: {Markup.Escape(ex.Message)}[/]");
                    // Fall through to create new file
                }
            }

            // Get Ollama versions and store for per-tag metadata
            _testOllamaVersion = await GetOllamaVersionAsync(_httpClient, _baseUrl);
            bool isJudgeRemote = false;
            if (_judgeEnabled && _judgeHttpClient != null && _cloudJudgeProvider == null)
            {
                // Get judge Ollama version (only if using Ollama for judging)
                isJudgeRemote = !_judgeBaseUrl!.Contains("localhost") && !_judgeBaseUrl.Contains("127.0.0.1");
                _judgeOllamaVersion = await GetOllamaVersionAsync(_judgeHttpClient, _judgeBaseUrl!);
            }

            // Create new results file
            _resultsFile = new BenchResultsFile
            {
                TestSuiteName = Path.GetFileName(_testSuiteFilePath),
                TestSuiteDigest = _testSuiteDigest,
                TestType = _testSuite.TestType,
                TestDescription = _testSuite.TestDescription,
                ModelName = _args.ModelName,
                JudgeModel = _judgeModelName,
                JudgeProvider = _cloudJudgeProvider?.ProviderName ?? (_judgeEnabled ? "ollama" : null),
                JudgeApiVersion = _cloudJudgeProvider?.GetApiVersion(),
                Options = new BenchTestOptions
                {
                    Temperature = _args.Temperature,
                    Seed = _args.Seed,
                    TopP = _args.TopP,
                    TopK = _args.TopK,
                    RepeatPenalty = _args.RepeatPenalty,
                    FrequencyPenalty = _args.FrequencyPenalty,
                    Timeout = _args.Timeout,
                    EnableThinking = _args.EnableThinking,
                    ThinkLevel = string.IsNullOrWhiteSpace(_args.ThinkLevel) ? null : _args.ThinkLevel
                },
                TestedAt = DateTime.UtcNow,
                OsyncVersion = GetOsyncVersion(),
                OllamaVersion = _testOllamaVersion,
                OllamaJudgeVersion = _judgeOllamaVersion,
                TestServerUrl = _baseUrl,
                JudgeServerUrl = isJudgeRemote ? _judgeBaseUrl : null,
                MaxContextLength = _testSuite.MaxContextLength,
                CategoryLimit = string.IsNullOrWhiteSpace(_args.Limit) ? null : _args.Limit,
                Results = new List<BenchQuantResult>()
            };

            return true;
        }

        private async Task SaveResultsFileAsync()
        {
            try
            {
                var outputPath = GetOutputFilePath();
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                // Use streaming serialization to avoid OutOfMemoryException with large results
                await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 65536, useAsync: true);
                await JsonSerializer.SerializeAsync(fileStream, _resultsFile, options);
            }
            catch (Exception ex)
            {
                Log($"[red]Error saving results: {ex.Message}[/]");
            }
        }

        private string GetOsyncVersion()
        {
            return OsyncProgram.GetFullVersion();
        }

        private async Task<string?> GetOllamaVersionAsync(HttpClient client, string baseUrl)
        {
            try
            {
                var response = await client.GetFromJsonAsync<RootVersion>($"{baseUrl}/api/version");
                return response?.version;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Verify server connectivity with a short timeout.
        /// Returns (success, version, errorMessage)
        /// </summary>
        private async Task<(bool Success, string? Version, string? ErrorMessage)> VerifyServerConnectionAsync(HttpClient client, string baseUrl, int timeoutSeconds = 10)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var response = await client.GetAsync($"{baseUrl}/api/version", cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var versionResponse = await response.Content.ReadFromJsonAsync<RootVersion>(cts.Token);
                    return (true, versionResponse?.version, null);
                }
                else
                {
                    return (false, null, $"Server returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase})");
                }
            }
            catch (TaskCanceledException)
            {
                return (false, null, $"Connection timed out after {timeoutSeconds} seconds - server may be unreachable");
            }
            catch (HttpRequestException ex)
            {
                return (false, null, $"Connection failed: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Unexpected error: {ex.Message}");
            }
        }

        #endregion

        #region Model Processing

        private async Task<List<string>> ExpandWildcardTagsAsync(List<string> patterns)
        {
            var result = new List<string>();

            try
            {
                var response = await _httpClient.GetFromJsonAsync<OllamaModelsResponse>($"{_baseUrl}/api/tags");
                var modelNames = response?.models?.Select(m => m.name).ToList() ?? new List<string>();

                foreach (var pattern in patterns)
                {
                    if (pattern.Contains('*') || pattern.Contains('?'))
                    {
                        // Wildcard pattern - match against available models
                        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                            .Replace("\\*", ".*")
                            .Replace("\\?", ".") + "$";

                        var matches = modelNames
                            .Where(m => System.Text.RegularExpressions.Regex.IsMatch(m, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                            .ToList();

                        result.AddRange(matches);
                    }
                    else
                    {
                        // Exact match - construct full name
                        var fullName = pattern.Contains(':') ? pattern : $"{_args.ModelName}:{pattern}";
                        result.Add(fullName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Could not expand wildcards: {ex.Message}[/]");
                return patterns.Select(p => p.Contains(':') ? p : $"{_args.ModelName}:{p}").ToList();
            }

            return result.Distinct().ToList();
        }

        private async Task ProcessQuantTagAsync(string quantTag)
        {
            // Initialize progress tracking for this model
            _currentModelTag = quantTag;
            _completedSuiteQuestions = 0;

            // Check if already tested
            var existingResult = _resultsFile.Results.FirstOrDefault(r =>
                r.Tag.Equals(quantTag, StringComparison.OrdinalIgnoreCase));

            // Check if we should skip testing
            bool skipTesting = false;
            if (existingResult != null && existingResult.IsComplete && !_args.Force)
            {
                // Compare tested categories with current test suite categories (respecting -L limit)
                var testedCategories = existingResult.CategoryResults?.Select(c => c.Category).ToHashSet() ?? new HashSet<string>();
                HashSet<string> requestedCategories;
                if (!string.IsNullOrWhiteSpace(_args.Limit))
                {
                    var limitCategory = _testSuite.Categories.FirstOrDefault(c =>
                        c.Name.Equals(_args.Limit, StringComparison.OrdinalIgnoreCase));
                    if (limitCategory != null)
                    {
                        var limitContext = limitCategory.ContextLength;
                        requestedCategories = _testSuite.Categories
                            .Where(c => c.ContextLength <= limitContext)
                            .Select(c => c.Name)
                            .ToHashSet();
                    }
                    else
                    {
                        requestedCategories = _testSuite.Categories.Select(c => c.Name).ToHashSet();
                    }
                }
                else
                {
                    requestedCategories = _testSuite.Categories.Select(c => c.Name).ToHashSet();
                }

                // Skip only if the tested categories match exactly what we're requesting
                if (testedCategories.SetEquals(requestedCategories))
                {
                    if (!_args.Rejudge)
                    {
                        Log($"[dim]Skipping {quantTag} (already tested with same categories, use --force to re-run)[/]");
                        return;
                    }
                    else
                    {
                        // Skip testing but will run judgment later
                        skipTesting = true;
                        Log($"[dim]Skipping testing for {quantTag} (already tested), will rejudge[/]");
                    }
                }
                else
                {
                    Log($"[yellow]  Re-running {quantTag} (categories changed: was ({string.Join(",", testedCategories)}), now ({string.Join(",", requestedCategories)}))[/]");
                }
            }

            // When skipping testing for rejudge, go straight to judgment phase
            if (skipTesting)
            {
                var quantResult = existingResult!;

                // Only judge if enabled and not cancelled
                if (_judgeEnabled && !_cancellationRequested)
                {
                    Log($"\n[bold]Rejudging {quantTag}[/]");
                    Log($"[dim]  Loading judge model...[/]");
                    var judgeLoadStopwatch = Stopwatch.StartNew();
                    await LoadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                    Log($"[dim]  Judge model loaded in {FormatDurationMs(judgeLoadStopwatch.ElapsedMilliseconds)}[/]");

                    Log($"\n[bold]  Judging answers...[/]");
                    await JudgeAllAnswersAsync(quantResult);

                    Log($"[dim]  Unloading judge model...[/]");
                    await UnloadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                }

                // Calculate scores and save
                if (!_cancellationRequested)
                {
                    quantResult.OverallScore = BenchScoring.CalculateOverallScore(quantResult);
                    quantResult.TotalQuestions = quantResult.CategoryResults?.Sum(c => c.TotalQuestions) ?? 0;
                    quantResult.CorrectAnswers = quantResult.CategoryResults?.Sum(c => c.CorrectAnswers) ?? 0;

                    await SaveResultsFileAsync();

                    Log($"[green]Completed {quantTag}: {quantResult.OverallScore:F1}% ({quantResult.CorrectAnswers}/{quantResult.TotalQuestions})[/]");
                }

                return;
            }

            // Check if model exists (only when actually testing)
            var modelExists = await CheckModelExistsAsync(quantTag);

            if (!modelExists && _args.OnDemand)
            {
                Log($"[cyan]Pulling {quantTag} on-demand...[/]");
                if (!await PullModelAsync(quantTag))
                {
                    Log($"[red]Failed to pull {quantTag}[/]");
                    return;
                }
                _currentModelPulledOnDemand = true;
                _modelsPulledThisSession.Add(quantTag);
            }
            else if (!modelExists)
            {
                Log($"[yellow]Model {quantTag} not found. Use --ondemand to pull automatically.[/]");
                return;
            }

            Log($"\n[bold]Testing {quantTag}[/]");

            try
            {
                // Get model info
                var modelInfo = await GetModelInfoAsync(quantTag);

                // Create or update result entry
                var quantResult = existingResult ?? new BenchQuantResult
                {
                    Tag = quantTag,
                    ModelName = _args.ModelName,
                    RepositoryUrl = _args.Repository
                };

                // If resuming an incomplete test (was cancelled), clear category results to restart fresh
                // This ensures the entire test sequence runs in a single chat session
                if (existingResult != null && !existingResult.IsComplete)
                {
                    Log($"[yellow]  Restarting incomplete test (discarding {quantResult.CategoryResults?.Count ?? 0} partial category results)[/]");
                    quantResult.CategoryResults = new List<BenchCategoryResult>();
                }

                if (modelInfo != null)
                {
                    quantResult.DiskSizeBytes = modelInfo.Size;
                    quantResult.Digest = modelInfo.Digest;
                    quantResult.ShortDigest = modelInfo.Digest?.Substring(0, Math.Min(12, modelInfo.Digest.Length));
                    quantResult.Family = modelInfo.Family ?? "";
                    quantResult.ParameterSize = modelInfo.ParameterSize ?? "";
                    quantResult.QuantizationType = modelInfo.QuantizationType ?? "";

                    // Check for cached pre-flight results with matching digest
                    var cachedPreflight = existingResult?.PreflightResult;
                    if (cachedPreflight != null &&
                        !string.IsNullOrEmpty(cachedPreflight.ModelDigest) &&
                        cachedPreflight.ModelDigest == modelInfo.Digest)
                    {
                        // Use cached pre-flight results
                        _thinkingEnabled = cachedPreflight.ThinkingEnabled;
                        _effectiveOverhead = cachedPreflight.EffectiveOverhead;
                        _modelMaxContextLength = cachedPreflight.ModelMaxContextLength;
                        _effectiveMaxContext = cachedPreflight.EffectiveMaxContext;
                        Log($"[dim]  Using cached pre-flight results (digest match: {cachedPreflight.ModelDigest.Substring(0, 12)})[/]");
                        Log($"[dim]  Thinking: {_thinkingEnabled}, Overhead: {_effectiveOverhead}, MaxCtx: {_modelMaxContextLength}[/]");
                    }

                    // Save current pre-flight results to quant result
                    quantResult.PreflightResult = new PreflightCheckResult
                    {
                        ModelDigest = modelInfo.Digest,
                        ThinkingEnabled = _thinkingEnabled,
                        EffectiveOverhead = _effectiveOverhead,
                        ModelMaxContextLength = _modelMaxContextLength,
                        EffectiveMaxContext = _effectiveMaxContext,
                        CheckedAt = DateTime.UtcNow
                    };
                }

                // For ctxtoolsbench, check tools support from template
                // Logic:
                // - Template has NO tools patterns → exit immediately (model cannot use tools)
                // - Template is empty/missing → run pre-flight (can't verify from template)
                // - Template HAS tools patterns → run pre-flight anyway (verify it works)
                bool templateHasToolsSupport = false;
                bool templateExists = !string.IsNullOrWhiteSpace(modelInfo?.Template);

                if (_testSuite.ToolsEnabled && modelInfo != null)
                {
                    if (templateExists)
                    {
                        templateHasToolsSupport = CheckToolsSupport(modelInfo.Template);
                        if (!templateHasToolsSupport)
                        {
                            // Template explicitly shows NO tools support - exit immediately
                            Log($"[red]  Model template does not have tools support[/]");
                            if (_args.Verbose)
                            {
                                var templatePreview = modelInfo.Template!.Length > 300
                                    ? modelInfo.Template.Substring(0, 300) + "..."
                                    : modelInfo.Template;
                                Log($"[dim]  Template preview: {Markup.Escape(templatePreview)}[/]");
                            }
                            Log($"[red]  Model does not support tools - cannot run ctxtoolsbench.[/]");
                            return;
                        }
                        else
                        {
                            Log($"[dim]  Model template has tools support[/]");
                        }
                    }
                    else
                    {
                        // Template is empty/missing - will rely on pre-flight test
                        Log($"[dim]  Model template not available - will verify tools via pre-flight test[/]");
                    }
                }

                quantResult.PulledOnDemand = _currentModelPulledOnDemand;
                quantResult.StartedAt = DateTime.UtcNow;

                // Set per-tag metadata (v1.2.9+)
                quantResult.JudgeModel = _judgeModelName;
                quantResult.JudgeProvider = _cloudJudgeProvider?.ProviderName ?? (_judgeEnabled ? "ollama" : null);
                quantResult.TestServerUrl = _baseUrl;
                quantResult.OsyncVersion = GetOsyncVersion();
                quantResult.OllamaVersion = _testOllamaVersion;
                quantResult.OllamaJudgeVersion = _judgeOllamaVersion;
                quantResult.TestOptions = new BenchTagOptions
                {
                    Seed = _args.Seed,
                    Timeout = _args.Timeout,
                    EnableThinking = _args.EnableThinking,
                    ThinkLevel = string.IsNullOrWhiteSpace(_args.ThinkLevel) ? null : _args.ThinkLevel,
                    Temperature = _args.Temperature
                };

                _currentPartialResult = quantResult;

                if (existingResult == null)
                    _resultsFile.Results.Add(quantResult);

                // Phase 1: Smart model loading
                // If only the same model is already loaded, just reload to reset expiration
                // Otherwise, unload other models first
                var loadStopwatch = Stopwatch.StartNew();
                var loadedModels = await GetLoadedModelsAsync();

                if (loadedModels.Count == 1 && loadedModels[0].Equals(quantTag, StringComparison.OrdinalIgnoreCase))
                {
                    // Same model is already loaded - just reload to reset expiration
                    Log($"[dim]  Model {quantTag} already loaded, reloading to reset expiration...[/]");
                    await LoadModelAsync(quantTag);
                    Log($"[dim]  Model reloaded in {FormatDurationMs(loadStopwatch.ElapsedMilliseconds)}[/]");
                }
                else
                {
                    // Different model(s) loaded or no models - unload others and load test model
                    if (loadedModels.Count > 0)
                    {
                        Log($"[dim]  Unloading {loadedModels.Count} other model(s)...[/]");
                        foreach (var model in loadedModels)
                        {
                            await UnloadModelAsync(model);
                        }
                    }
                    Log($"[dim]  Loading test model...[/]");
                    await LoadModelAsync(quantTag);
                    Log($"[dim]  Model loaded in {FormatDurationMs(loadStopwatch.ElapsedMilliseconds)}[/]");
                }

                // Phase 1.5: For ctxtoolsbench, run pre-flight tools test (unless cached)
                // This is a functional test - if model can't use tools, it will fail the entire test
                if (_testSuite.ToolsEnabled)
                {
                    // Check for cached tools pre-flight result
                    var cachedToolsPreflight = existingResult?.PreflightResult?.ToolsPreflightPassed;
                    if (cachedToolsPreflight == true &&
                        existingResult?.PreflightResult?.ModelDigest == modelInfo?.Digest)
                    {
                        Log($"[dim]  Using cached tools pre-flight result (passed)[/]");
                    }
                    else if (cachedToolsPreflight == false &&
                             existingResult?.PreflightResult?.ModelDigest == modelInfo?.Digest)
                    {
                        Log($"[red]  Skipping {quantTag} - tools pre-flight previously failed[/]");
                        if (existingResult == null)
                            _resultsFile.Results.Remove(quantResult);
                        return;
                    }
                    else
                    {
                        // Run tools pre-flight test
                        var preflightPassed = await RunToolsPreflightTestAsync(quantTag);

                        // Save result to pre-flight cache
                        if (quantResult.PreflightResult != null)
                        {
                            quantResult.PreflightResult.ToolsPreflightPassed = preflightPassed;
                        }

                        if (!preflightPassed)
                        {
                            Log($"[red]  Skipping {quantTag} - tools pre-flight test failed[/]");
                            // Remove the partial result we added earlier
                            if (existingResult == null)
                                _resultsFile.Results.Remove(quantResult);
                            return;
                        }
                    }
                }

                // For parallel mode, load judge model before testing and start consumer
                bool parallelJudgmentActive = false;
                if (_judgeEnabled && IsParallelMode() && !_cancellationRequested)
                {
                    Log($"[dim]  Loading judge model for parallel judgment...[/]");
                    var judgeLoadStopwatch = Stopwatch.StartNew();
                    await LoadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                    Log($"[dim]  Judge model loaded in {FormatDurationMs(judgeLoadStopwatch.ElapsedMilliseconds)}[/]");
                    StartJudgmentConsumer();
                    parallelJudgmentActive = true;
                    Log($"[cyan]  Parallel judgment mode active - answers will be judged as testing proceeds[/]");
                }

                // Phase 2: Run tests for each category (without judging in serial mode, with parallel judging if enabled)
                // Fail-fast: if any API error occurs after retries, BenchFatalErrorException is thrown
                try
                {
                    await RunCategoryTestsAsync(quantTag, quantResult);
                }
                catch (BenchFatalErrorException ex)
                {
                    Log($"[red]  Test aborted: {ex.Message}[/]");
                    Log($"[yellow]  Results will not be saved. Check model availability and configuration.[/]");
                    // Remove the result we added earlier
                    if (existingResult == null)
                        _resultsFile.Results.Remove(quantResult);

                    // Clean up parallel judgment if active
                    if (parallelJudgmentActive)
                    {
                        _judgmentQueueComplete = true;
                        await UnloadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                    }
                    return;
                }

                // Phase 3: Handle judgment based on mode
                if (_judgeEnabled && !_cancellationRequested)
                {
                    if (parallelJudgmentActive)
                    {
                        // Parallel mode: wait for pending judgments, then unload judge
                        Log($"\n[dim]  Unloading test model...[/]");
                        await UnloadModelAsync(quantTag);

                        await WaitForPendingJudgmentsAsync();

                        Log($"[dim]  Unloading judge model...[/]");
                        await UnloadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                    }
                    else
                    {
                        // Serial mode: unload test, load judge, judge all, unload judge
                        Log($"\n[dim]  Unloading test model...[/]");
                        await UnloadModelAsync(quantTag);

                        Log($"[dim]  Loading judge model...[/]");
                        var judgeLoadStopwatch = Stopwatch.StartNew();
                        await LoadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                        Log($"[dim]  Judge model loaded in {FormatDurationMs(judgeLoadStopwatch.ElapsedMilliseconds)}[/]");

                        // Phase 4: Judge all answers
                        Log($"\n[bold]  Judging answers...[/]");
                        await JudgeAllAnswersAsync(quantResult);

                        // Phase 5: Unload judge model
                        Log($"[dim]  Unloading judge model...[/]");
                        await UnloadModelAsync(_judgeModelName!, _judgeHttpClient, _judgeBaseUrl);
                    }
                }

                // Only mark as complete if all categories were tested (not cancelled)
                if (!_cancellationRequested)
                {
                    // Calculate overall score
                    quantResult.OverallScore = BenchScoring.CalculateOverallScore(quantResult);
                    quantResult.TotalQuestions = quantResult.CategoryResults?.Sum(c => c.TotalQuestions) ?? 0;
                    quantResult.CorrectAnswers = quantResult.CategoryResults?.Sum(c => c.CorrectAnswers) ?? 0;
                    quantResult.CompletedAt = DateTime.UtcNow;
                    quantResult.IsComplete = true;

                    await SaveResultsFileAsync();

                    Log($"[green]Completed {quantTag}: {quantResult.OverallScore:F1}% ({quantResult.CorrectAnswers}/{quantResult.TotalQuestions})[/]");
                }
                else
                {
                    // Save partial progress (already saved after each category, but ensure final state is saved)
                    quantResult.OverallScore = BenchScoring.CalculateOverallScore(quantResult);
                    quantResult.TotalQuestions = quantResult.CategoryResults?.Sum(c => c.TotalQuestions) ?? 0;
                    quantResult.CorrectAnswers = quantResult.CategoryResults?.Sum(c => c.CorrectAnswers) ?? 0;
                    // IsComplete stays false for partial results
                    await SaveResultsFileAsync();

                    Log($"[yellow]Partial progress saved for {quantTag}: {quantResult.OverallScore:F1}% ({quantResult.CorrectAnswers}/{quantResult.TotalQuestions})[/]");
                }
            }
            finally
            {
                // Clean up on-demand pulled model
                if (_currentModelPulledOnDemand && _args.OnDemand)
                {
                    Log($"[dim]Removing on-demand model {quantTag}...[/]");
                    await DeleteModelAsync(quantTag);
                }
                _currentModelPulledOnDemand = false;
                _currentPartialResult = null;
            }
        }

        private async Task<bool> CheckModelExistsAsync(string modelName)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<OllamaModelsResponse>($"{_baseUrl}/api/tags");
                return response?.models?.Any(m => m.name.Equals(modelName, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> PullModelAsync(string modelName)
        {
            // Two-phase retry strategy for HuggingFace rate limiting:
            // Phase 1: Quick retries (2s delay) - catches IP changes
            // Phase 2: Slow retries (30s or HF API delay) - waits out rate limits
            const int maxRetriesPerPhase = 50;
            const int quickRetryDelayMs = 2000;
            const int slowRetryDelaySeconds = 30;
            const int normalRetryDelayMs = 2000;

            bool isHuggingFaceModel = modelName.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase);
            bool shownHfTokenWarning = false;
            int totalPhases = isHuggingFaceModel ? 2 : 1;

            for (int phase = 1; phase <= totalPhases; phase++)
            {
                bool isSlowPhase = phase == 2;
                string phaseDesc = isSlowPhase ? "slow" : "quick";

                if (phase == 2)
                {
                    Log($"[yellow]Entering slow retry phase (30s+ delays)...[/]");
                }

                for (int attempt = 1; attempt <= maxRetriesPerPhase; attempt++)
                {
                    int globalAttempt = (phase - 1) * maxRetriesPerPhase + attempt;
                    int totalMaxRetries = totalPhases * maxRetriesPerPhase;

                    try
                    {
                        if (globalAttempt == 1)
                        {
                            Log($"[cyan]Pulling model: {modelName}[/]");
                        }
                        else
                        {
                            Log($"[yellow]Retry {globalAttempt}/{totalMaxRetries} ({phaseDesc}): Pulling model...[/]");
                        }

                    var pullRequest = new { name = modelName, stream = true };
                    var content = new StringContent(
                        JsonSerializer.Serialize(pullRequest),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/pull")
                    {
                        Content = content
                    };

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode)
                    {
                        Log($"[red]Error: Failed to pull model (HTTP {response.StatusCode})[/]");
                        if (attempt < maxRetriesPerPhase)
                        {
                            await Task.Delay(normalRetryDelayMs);
                            continue;
                        }
                        break; // End of this phase's retries
                    }

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: false);

                    string? lastStatus = null;
                    string? currentDigest = null;
                    int layersCompletedThisAttempt = 0;
                    long totalSize = 0;
                    long completedSize = 0;
                    long lastCompletedSize = 0;
                    int lastPercent = -1;
                    bool hasError = false;
                    string? errorMessage = null;
                    DateTime lastSpeedUpdate = DateTime.UtcNow;
                    double currentSpeed = 0;

                    while (!reader.EndOfStream)
                    {
                        // Check for cancellation
                        if (_cancellationRequested)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                        }

                        var line = await reader.ReadLineAsync(_cts.Token);
                        if (string.IsNullOrEmpty(line)) continue;

                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;

                            // Check for error
                            if (root.TryGetProperty("error", out var errorElement))
                            {
                                var error = errorElement.GetString();
                                if (!string.IsNullOrEmpty(error))
                                {
                                    hasError = true;
                                    errorMessage = error;
                                    break;
                                }
                            }

                            // Get digest
                            if (root.TryGetProperty("digest", out var digestElement))
                            {
                                var digest = digestElement.GetString();
                                if (!string.IsNullOrEmpty(digest) && digest != currentDigest)
                                {
                                    // Track completed layer when moving to a new digest
                                    if (!string.IsNullOrEmpty(currentDigest) && completedSize > 0 && totalSize > 0 && completedSize >= totalSize * 0.99)
                                    {
                                        layersCompletedThisAttempt++;
                                    }
                                    currentDigest = digest; // Store full digest for comparison
                                    // Reset for new layer
                                    lastCompletedSize = 0;
                                    currentSpeed = 0;
                                    lastSpeedUpdate = DateTime.UtcNow;
                                }
                            }

                            // Get status
                            if (root.TryGetProperty("status", out var statusElement))
                            {
                                var status = statusElement.GetString();
                                if (!string.IsNullOrEmpty(status) && status != lastStatus)
                                {
                                    if (lastPercent >= 0)
                                    {
                                        try { System.Console.Write("\r" + new string(' ', Math.Min(System.Console.WindowWidth - 1, 120)) + "\r"); System.Console.Out.Flush(); } catch { }
                                    }
                                    Log($"[dim]{Markup.Escape(status)}[/]");
                                    lastStatus = status;
                                    lastPercent = -1;
                                    // Don't reset currentDigest here - let the digest check handle layer transitions
                                    // Don't reset currentSpeed here - preserve speed display across status changes
                                }
                            }

                            // Get progress
                            if (root.TryGetProperty("total", out var totalElement))
                                totalSize = totalElement.GetInt64();

                            if (root.TryGetProperty("completed", out var completedElement))
                            {
                                completedSize = completedElement.GetInt64();
                                if (totalSize > 0)
                                {
                                    var percent = (int)((completedSize * 100) / totalSize);

                                    // Calculate speed
                                    var now = DateTime.UtcNow;
                                    var timeSinceLastUpdate = (now - lastSpeedUpdate).TotalSeconds;

                                    // Initialize baseline on first progress (or after reset/resume)
                                    // This must come first to handle resume correctly - don't calculate speed on first update
                                    if (lastCompletedSize == 0)
                                    {
                                        if (completedSize > 0)
                                        {
                                            lastCompletedSize = completedSize;
                                            lastSpeedUpdate = now;
                                        }
                                    }
                                    // Calculate speed when we have progress and enough time has elapsed
                                    else if (completedSize > lastCompletedSize && timeSinceLastUpdate >= 0.1)
                                    {
                                        var bytesSinceLastUpdate = completedSize - lastCompletedSize;
                                        var instantSpeed = bytesSinceLastUpdate / timeSinceLastUpdate;

                                        // Exponential moving average for smoother speed display
                                        currentSpeed = currentSpeed == 0 ? instantSpeed : (currentSpeed * 0.7 + instantSpeed * 0.3);
                                        lastCompletedSize = completedSize;
                                        lastSpeedUpdate = now;
                                    }

                                    if (percent != lastPercent || timeSinceLastUpdate >= 0.25)
                                    {
                                        int barWidth = 25;
                                        int filled = (int)(barWidth * completedSize / totalSize);
                                        var bar = new string('█', filled) + new string('░', barWidth - filled);

                                        var sizeInfo = $"{FormatBytes(completedSize)}/{FormatBytes(totalSize)}";
                                        var speedInfo = currentSpeed > 0 ? $"{FormatBytes((long)currentSpeed)}/s" : "---";

                                        var etaInfo = "";
                                        if (currentSpeed > 0)
                                        {
                                            var remaining = totalSize - completedSize;
                                            var etaSeconds = remaining / currentSpeed;
                                            if (etaSeconds < 60)
                                                etaInfo = $" ETA: {etaSeconds:F0}s";
                                            else if (etaSeconds < 3600)
                                                etaInfo = $" ETA: {etaSeconds / 60:F0}m {etaSeconds % 60:F0}s";
                                            else
                                                etaInfo = $" ETA: {etaSeconds / 3600:F0}h {(etaSeconds % 3600) / 60:F0}m";
                                        }

                                        var digestShort = !string.IsNullOrEmpty(currentDigest) && currentDigest.Length > 19
                                            ? currentDigest.Substring(0, 19) : currentDigest;
                                        var digestInfo = !string.IsNullOrEmpty(digestShort) ? $"[{digestShort}]" : "";
                                        var progressLine = $"  {digestInfo} {bar} {percent,3}% ({sizeInfo}) {speedInfo}{etaInfo}";

                                        try
                                        {
                                            var maxWidth = Math.Min(System.Console.WindowWidth - 1, 120);
                                            if (progressLine.Length > maxWidth)
                                                progressLine = progressLine.Substring(0, maxWidth);
                                            var padding = Math.Max(0, maxWidth - progressLine.Length);
                                            System.Console.Write($"\r{progressLine}{new string(' ', padding)}");
                                            System.Console.Out.Flush();
                                        }
                                        catch { }
                                        lastPercent = percent;
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    if (lastPercent >= 0)
                        System.Console.WriteLine();

                    if (hasError)
                    {
                        Log($"[red]Error: {Markup.Escape(errorMessage ?? "Unknown error")}[/]");

                        // Reset retry counter if progress was made (layers were downloaded)
                        // This prevents exhausting retries when making steady progress despite intermittent rate limits
                        if (layersCompletedThisAttempt > 0)
                        {
                            Log($"[dim]Progress made ({layersCompletedThisAttempt} layer(s) completed), resetting retry counter[/]");
                            attempt = 0; // Will be 1 on next iteration
                        }

                        // Check if this is a "model not found" error - don't retry, exit immediately
                        bool isNotFoundError = errorMessage != null &&
                            (errorMessage.Contains("file does not exist", StringComparison.OrdinalIgnoreCase) ||
                             errorMessage.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
                             errorMessage.Contains("does not exist", StringComparison.OrdinalIgnoreCase));

                        if (isNotFoundError)
                        {
                            Log($"[red]Model does not exist in registry. Not retrying.[/]");
                            return false;
                        }

                        // Check if this is a rate limit error (429 or contains "rate limit")
                        bool isRateLimitError = errorMessage != null &&
                            (errorMessage.Contains("429") || errorMessage.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

                        if (attempt < maxRetriesPerPhase)
                        {
                            if (isRateLimitError && isHuggingFaceModel)
                            {
                                // Show HF_TOKEN warning only once when rate limit is hit
                                if (!shownHfTokenWarning)
                                {
                                    shownHfTokenWarning = true;
                                    var hfToken = Environment.GetEnvironmentVariable("HF_TOKEN");
                                    if (string.IsNullOrEmpty(hfToken))
                                    {
                                        Log($"[yellow]Tip: Set HF_TOKEN environment variable to avoid rate limiting.[/]");
                                    }
                                }

                                if (isSlowPhase)
                                {
                                    // Slow phase: use HF API delay or default 30s
                                    int delaySeconds = slowRetryDelaySeconds;
                                    var resetSeconds = await GetHuggingFaceRateLimitResetSecondsAsync();
                                    if (resetSeconds.HasValue && resetSeconds.Value > 0)
                                    {
                                        delaySeconds = Math.Min(resetSeconds.Value + 5, 300);
                                        Log($"[yellow]Rate limit detected, waiting {delaySeconds}s (from HF API) before retry...[/]");
                                    }
                                    else
                                    {
                                        Log($"[yellow]Rate limit detected, waiting {delaySeconds}s before retry...[/]");
                                    }
                                    await Task.Delay(delaySeconds * 1000);
                                }
                                else
                                {
                                    // Quick phase: 2s delay to catch IP changes
                                    Log($"[yellow]Rate limit detected, quick retry in {quickRetryDelayMs / 1000}s...[/]");
                                    await Task.Delay(quickRetryDelayMs);
                                }
                            }
                            else if (isRateLimitError)
                            {
                                // Non-HF rate limit
                                Log($"[yellow]Rate limit detected, waiting {slowRetryDelaySeconds}s before retry...[/]");
                                await Task.Delay(slowRetryDelaySeconds * 1000);
                            }
                            else
                            {
                                await Task.Delay(normalRetryDelayMs);
                            }
                            continue;
                        }
                        // End of this phase's retries - continue to next phase if available
                        break;
                    }

                    Log($"[green]✓ Model pulled successfully: {modelName}[/]");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    Log($"[yellow]Pull cancelled[/]");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"[red]Error pulling model: {ex.Message}[/]");

                    // Check if this is a rate limit error
                    bool isRateLimitError = ex.Message.Contains("429") ||
                        ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

                    if (attempt < maxRetriesPerPhase)
                    {
                        if (isRateLimitError && isHuggingFaceModel)
                        {
                            
                            // Show HF_TOKEN warning only once
                            if (!shownHfTokenWarning)
                            {
                                shownHfTokenWarning = true;
                                var hfToken = Environment.GetEnvironmentVariable("HF_TOKEN");
                                if (string.IsNullOrEmpty(hfToken))
                                {
                                    Log($"[yellow]Tip: Set HF_TOKEN environment variable to avoid rate limiting.[/]");
                                }
                            }

                            if (isSlowPhase)
                            {
                                // Slow phase: use HF API delay or default 30s
                                int delaySeconds = slowRetryDelaySeconds;
                                var resetSeconds = await GetHuggingFaceRateLimitResetSecondsAsync();
                                if (resetSeconds.HasValue && resetSeconds.Value > 0)
                                {
                                    delaySeconds = Math.Min(resetSeconds.Value + 5, 300);
                                    Log($"[yellow]Rate limit detected, waiting {delaySeconds}s (from HF API) before retry...[/]");
                                }
                                else
                                {
                                    Log($"[yellow]Rate limit detected, waiting {delaySeconds}s before retry...[/]");
                                }
                                await Task.Delay(delaySeconds * 1000);
                            }
                            else
                            {
                                // Quick phase: 2s delay to catch IP changes
                                Log($"[yellow]Rate limit detected, quick retry in {quickRetryDelayMs / 1000}s...[/]");
                                await Task.Delay(quickRetryDelayMs);
                            }
                        }
                        else if (isRateLimitError)
                        {
                            // Non-HF rate limit
                            Log($"[yellow]Rate limit detected, waiting {slowRetryDelaySeconds}s before retry...[/]");
                            await Task.Delay(slowRetryDelaySeconds * 1000);
                        }
                        else
                        {
                            await Task.Delay(normalRetryDelayMs);
                        }
                        continue;
                    }
                    // End of this phase's retries - continue to next phase if available
                    break;
                }
                }
            }

            int totalRetries = totalPhases * maxRetriesPerPhase;
            Log($"[red]Error: Max retries ({totalRetries}) exceeded for model: {modelName}[/]");
            return false;
        }

        /// <summary>
        /// Query HuggingFace API to get the rate limit reset time in seconds.
        /// Returns null if HF_TOKEN is not set or if the query fails.
        /// </summary>
        private async Task<int?> GetHuggingFaceRateLimitResetSecondsAsync()
        {
            try
            {
                var hfToken = Environment.GetEnvironmentVariable("HF_TOKEN");
                if (string.IsNullOrEmpty(hfToken))
                    return null;

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", hfToken);
                client.Timeout = TimeSpan.FromSeconds(10);

                // Make a lightweight request to HuggingFace API to get rate limit headers
                var response = await client.GetAsync("https://huggingface.co/api/whoami");

                // Parse RateLimit header: "resolvers";r=[remaining];t=[seconds until reset]
                if (response.Headers.TryGetValues("RateLimit", out var rateLimitValues))
                {
                    var rateLimitHeader = rateLimitValues.FirstOrDefault();
                    if (!string.IsNullOrEmpty(rateLimitHeader))
                    {
                        // Look for t=[number] pattern
                        var match = System.Text.RegularExpressions.Regex.Match(rateLimitHeader, @"t=(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out var seconds))
                        {
                            return seconds > 0 ? seconds : null;
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private async Task DeleteModelAsync(string modelName)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/delete")
                {
                    Content = JsonContent.Create(new { name = modelName })
                };
                await _httpClient.SendAsync(request);
            }
            catch { }
        }

        private async Task<ModelInfoResult?> GetModelInfoAsync(string modelName)
        {
            try
            {
                // Get model details from /api/show
                var request = new { name = modelName };
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/show", request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                var details = root.TryGetProperty("details", out var d) ? d : default;

                // Get model size and digest from /api/tags endpoint (like QcCommand does)
                // The /api/show response doesn't reliably include the size at root level
                long sizeBytes = 0;
                string? digest = null;

                var tagsResponse = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                if (tagsResponse.IsSuccessStatusCode)
                {
                    var tagsJson = await tagsResponse.Content.ReadAsStringAsync();
                    using var tagsDoc = JsonDocument.Parse(tagsJson);
                    if (tagsDoc.RootElement.TryGetProperty("models", out var modelsArray))
                    {
                        foreach (var model in modelsArray.EnumerateArray())
                        {
                            var name = model.TryGetProperty("name", out var n) ? n.GetString() : null;
                            if (name?.Equals(modelName, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                sizeBytes = model.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                                // Extract digest - Ollama returns it in format "sha256:abc123..."
                                if (model.TryGetProperty("digest", out var digestProp))
                                {
                                    var rawDigest = digestProp.GetString();
                                    if (!string.IsNullOrEmpty(rawDigest))
                                    {
                                        digest = rawDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                                            ? rawDigest.Substring(7)
                                            : rawDigest;
                                    }
                                }
                                break;
                            }
                        }
                    }
                }

                return new ModelInfoResult
                {
                    Size = sizeBytes,
                    Digest = digest,
                    Family = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("family", out var f) ? f.GetString() : null,
                    ParameterSize = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("parameter_size", out var ps) ? ps.GetString() : null,
                    QuantizationType = details.ValueKind == JsonValueKind.Object && details.TryGetProperty("quantization_level", out var q) ? q.GetString() : null,
                    Template = root.TryGetProperty("template", out var t) ? t.GetString() : null
                };
            }
            catch
            {
                return null;
            }
        }

        private class ModelInfoResult
        {
            public long Size { get; set; }
            public string? Digest { get; set; }
            public string? Family { get; set; }
            public string? ParameterSize { get; set; }
            public string? QuantizationType { get; set; }
            public string? Template { get; set; }
        }

        /// <summary>
        /// Get the model's maximum context length from the Ollama API.
        /// Returns the context length from model_info.{architecture}.context_length.
        /// </summary>
        private async Task<int> GetModelMaxContextLengthAsync(string modelName)
        {
            try
            {
                var request = new { name = modelName };
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/show", request);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                var root = doc.RootElement;
                if (root.TryGetProperty("model_info", out var modelInfo))
                {
                    // First get the architecture
                    string? architecture = null;
                    if (modelInfo.TryGetProperty("general.architecture", out var archElement))
                    {
                        architecture = archElement.GetString();
                    }

                    if (!string.IsNullOrEmpty(architecture))
                    {
                        // Look for {architecture}.context_length
                        var contextLengthKey = $"{architecture}.context_length";
                        if (modelInfo.TryGetProperty(contextLengthKey, out var ctxElement))
                        {
                            return ctxElement.GetInt32();
                        }
                    }

                    // Fallback: scan for any *.context_length property
                    foreach (var prop in modelInfo.EnumerateObject())
                    {
                        if (prop.Name.EndsWith(".context_length") && prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            return prop.Value.GetInt32();
                        }
                    }
                }

                // Default fallback
                Log($"[yellow]Warning: Could not determine model's max context length, using default 131072[/]");
                return 131072;
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Error getting model context length: {ex.Message}, using default 131072[/]");
                return 131072;
            }
        }

        /// <summary>
        /// Pre-flight check to detect if the model has thinking mode enabled.
        /// Sends a simple question designed to trigger thinking and checks for:
        /// - Separate thinking content in response
        /// - &lt;/think&gt; tag in the answer
        /// </summary>
        private async Task<bool> DetectThinkingEnabledAsync(string modelName)
        {
            Log($"[dim]Detecting thinking mode for model...[/]");

            try
            {
                // Simple question designed to trigger thinking
                var thinkingTriggerPrompt = "What is 15 + 27? Think step by step.";

                var messages = new[]
                {
                    new { role = "user", content = thinkingTriggerPrompt }
                };

                var requestBody = new
                {
                    model = modelName,
                    messages = messages,
                    stream = true,
                    options = new { num_ctx = 4096, num_predict = 512 }
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json"
                );

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
                response.EnsureSuccessStatusCode();

                var fullResponse = new StringBuilder();
                bool hasThinkingContent = false;

                await using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                using var reader = new StreamReader(stream);

                while (!reader.EndOfStream && !timeoutCts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(timeoutCts.Token);
                    if (string.IsNullOrEmpty(line)) continue;

                    try
                    {
                        using var doc = JsonDocument.Parse(line);
                        var root = doc.RootElement;

                        // Check for separate thinking content (some models provide this)
                        if (root.TryGetProperty("message", out var msgElement))
                        {
                            // Check for thinking field (qwen3 style)
                            if (msgElement.TryGetProperty("thinking", out var thinkingElement))
                            {
                                var thinkingText = thinkingElement.GetString();
                                if (!string.IsNullOrEmpty(thinkingText))
                                {
                                    hasThinkingContent = true;
                                }
                            }

                            // Check for thinking_content field (DeepSeek style)
                            if (msgElement.TryGetProperty("thinking_content", out var thinkingContentElement))
                            {
                                var thinkingText = thinkingContentElement.GetString();
                                if (!string.IsNullOrEmpty(thinkingText))
                                {
                                    hasThinkingContent = true;
                                }
                            }

                            // Accumulate response content
                            if (msgElement.TryGetProperty("content", out var contentElement))
                            {
                                var text = contentElement.GetString();
                                if (!string.IsNullOrEmpty(text))
                                {
                                    fullResponse.Append(text);
                                }
                            }
                        }

                        // Check for done flag
                        if (root.TryGetProperty("done", out var doneElement) && doneElement.GetBoolean())
                        {
                            break;
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed JSON lines
                    }
                }

                var responseText = fullResponse.ToString();

                // Check for <think> or </think> tags in the response (common thinking marker)
                bool hasThinkTag = responseText.Contains("<think>", StringComparison.OrdinalIgnoreCase) ||
                                   responseText.Contains("</think>", StringComparison.OrdinalIgnoreCase);

                bool thinkingDetected = hasThinkingContent || hasThinkTag;

                if (thinkingDetected)
                {
                    var reason = hasThinkingContent ? "thinking field in response" : "<think> tags";
                    Log($"[cyan]Thinking mode detected ({reason})[/]");
                }
                else
                {
                    Log($"[dim]Thinking mode not detected[/]");
                }

                return thinkingDetected;
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Thinking detection failed: {ex.Message}[/]");
                Log($"[dim]Assuming thinking mode is NOT enabled[/]");
                return false;
            }
        }

        /// <summary>
        /// Resolve context length settings based on model capabilities and test suite requirements.
        /// This method handles:
        /// - Detecting model's max context length
        /// - Detecting thinking mode
        /// - Auto-limiting categories when test suite exceeds model capacity
        /// - Calculating effective context with overhead
        /// </summary>
        private async Task<bool> ResolveContextLengthSettingsAsync(string modelName)
        {
            // Get model's max context length
            _modelMaxContextLength = await GetModelMaxContextLengthAsync(modelName);
            Log($"[dim]Model max context length: {_modelMaxContextLength:N0} tokens[/]");

            // Detect thinking mode
            _thinkingEnabled = await DetectThinkingEnabledAsync(modelName);

            // Select appropriate overhead
            _effectiveOverhead = _thinkingEnabled
                ? _testSuite.ContextLengthOverheadThinking
                : _testSuite.ContextLengthOverhead;

            Log($"[dim]Using context overhead: {_effectiveOverhead:N0} tokens ({(_thinkingEnabled ? "thinking" : "standard")})[/]");

            // Determine the effective max context we can use
            int requestedMaxContext;
            string limitSource;

            if (!string.IsNullOrWhiteSpace(_args.Limit))
            {
                // User specified a limit with -L
                var limitCategory = _testSuite.Categories.FirstOrDefault(c =>
                    c.Name.Equals(_args.Limit, StringComparison.OrdinalIgnoreCase));

                if (limitCategory == null)
                {
                    Log($"[red]Error: Category '{_args.Limit}' not found in test suite[/]");
                    var available = string.Join(", ", _testSuite.Categories.Select(c => c.Name));
                    Log($"[dim]Available categories: {available}[/]");
                    return false;
                }

                requestedMaxContext = limitCategory.ContextLength;
                limitSource = $"-L {_args.Limit}";

                // Check if requested limit exceeds model capacity (with 2% margin and k-base tolerance)
                if (!CategoryFitsWithinContext(requestedMaxContext, _modelMaxContextLength))
                {
                    // Find highest category that fits within model's max context
                    var fittingCategory = _testSuite.Categories
                        .Where(c => CategoryFitsWithinContext(c.ContextLength, _modelMaxContextLength))
                        .OrderByDescending(c => c.ContextLength)
                        .FirstOrDefault();

                    if (fittingCategory == null)
                    {
                        Log($"[red]Error: No categories fit within model's max context length ({_modelMaxContextLength:N0})[/]");
                        return false;
                    }

                    Log($"[yellow]Warning: Requested limit {_args.Limit} ({requestedMaxContext:N0}) exceeds model max context ({_modelMaxContextLength:N0})[/]");
                    Log($"[yellow]Auto-limiting to category: {fittingCategory.Name}[/]");

                    requestedMaxContext = fittingCategory.ContextLength;
                    limitSource = $"auto-limited to {fittingCategory.Name} (model max)";
                    _args.Limit = fittingCategory.Name;
                }
                else if (requestedMaxContext + _effectiveOverhead <= _modelMaxContextLength)
                {
                    // Limit is within model capacity with overhead
                    requestedMaxContext += _effectiveOverhead;
                    limitSource = $"-L {_args.Limit} + {_effectiveOverhead} overhead";
                }
                // else: limit is close to model max, use as-is without additional overhead
            }
            else
            {
                // No limit specified - check if test suite max exceeds model max (with 2% margin and k-base tolerance)
                if (!CategoryFitsWithinContext(_testSuite.MaxContextLength, _modelMaxContextLength))
                {
                    // Auto-limit to highest category that fits
                    var fittingCategory = _testSuite.Categories
                        .Where(c => CategoryFitsWithinContext(c.ContextLength, _modelMaxContextLength))
                        .OrderByDescending(c => c.ContextLength)
                        .FirstOrDefault();

                    if (fittingCategory == null)
                    {
                        Log($"[red]Error: No categories fit within model's max context length ({_modelMaxContextLength:N0})[/]");
                        return false;
                    }

                    Log($"[yellow]Test suite max ({_testSuite.MaxContextLength:N0}) exceeds model max ({_modelMaxContextLength:N0})[/]");
                    Log($"[yellow]Auto-limiting to category: {fittingCategory.Name}[/]");

                    requestedMaxContext = fittingCategory.ContextLength;
                    limitSource = $"auto-limited to {fittingCategory.Name} (model max)";
                    _args.Limit = fittingCategory.Name;
                }
                else
                {
                    // Test suite fits within model capacity
                    requestedMaxContext = _testSuite.MaxContextLength;
                    limitSource = "test suite max";

                    // Check if we can add overhead
                    if (requestedMaxContext + _effectiveOverhead <= _modelMaxContextLength)
                    {
                        requestedMaxContext += _effectiveOverhead;
                        limitSource = $"test suite max + {_effectiveOverhead} overhead";
                    }
                }
            }

            // Apply the limit to categories if needed
            if (!string.IsNullOrWhiteSpace(_args.Limit))
            {
                var limitIndex = _testSuite.Categories.FindIndex(c =>
                    c.Name.Equals(_args.Limit, StringComparison.OrdinalIgnoreCase));

                if (limitIndex >= 0)
                {
                    _testSuite.Categories = _testSuite.Categories.Take(limitIndex + 1).ToList();
                }
            }

            _effectiveMaxContext = requestedMaxContext;

            // Print summary
            Log($"[bold]Context length for testing: {_effectiveMaxContext:N0} tokens[/]");
            Log($"[dim]  Reason: {limitSource}[/]");
            Log($"[dim]  Categories: {string.Join(", ", _testSuite.Categories.Select(c => c.Name))}[/]");

            return true;
        }

        /// <summary>
        /// Check if a category context length fits within a model's max context.
        /// Applies a 2% margin and considers both 1000-based and 1024-based k definitions.
        /// For example: 256k can be 262144 (256*1024) or 256000 (256*1000).
        /// </summary>
        private static bool CategoryFitsWithinContext(int categoryContextLength, int modelMaxContext)
        {
            // Direct comparison with 2% margin
            if (categoryContextLength <= modelMaxContext * 1.02)
                return true;

            // Check if the category (1024-based) fits when converted to 1000-based equivalent
            // e.g., 262144 (256*1024) → 256000 (256*1000)
            // This handles models that define context as 256000 instead of 262144
            if (categoryContextLength > 1024)
            {
                // Get the 'k' value: for 262144 → 256
                int kValue1024 = categoryContextLength / 1024;
                // Convert to 1000-based: 256 * 1000 = 256000
                int equivalent1000Based = kValue1024 * 1000;

                // If the 1000-based equivalent fits with margin, accept it
                if (equivalent1000Based <= modelMaxContext * 1.02)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a model's template indicates tools support.
        /// Returns true if the template contains tool-related patterns.
        /// </summary>
        private static bool CheckToolsSupport(string? template)
        {
            if (string.IsNullOrEmpty(template))
                return false;

            // Common patterns in model templates that indicate tools support
            var toolsPatterns = new[]
            {
                "{{- if .Tools }}",           // Go template for tools
                "{{- range .ToolCalls }}",    // Go template for tool calls
                "{{#if tools}}",              // Mustache/Handlebars
                "{{#tools}}",                 // Mustache tools block
                "<tool_call>",                // Common tool call marker
                "<|tool|>",                   // Alternative tool marker
                "<|tool_call|>",              // Qwen-style tool call
                "[TOOL_CALLS]",               // Mistral-style tool calls
                "<<tool_call>>",              // Alternative marker
                "\"type\": \"function\"",     // JSON function definition
                ".ToolCalls",                 // Go template reference
                "tool_calls",                 // Generic reference
                "<tools>",                    // XML-style tools section
                "<function_call>",            // Function call marker
                "<|python_tag|>",             // Code execution (can indicate tools)
            };

            var templateLower = template.ToLowerInvariant();
            return toolsPatterns.Any(p => templateLower.Contains(p.ToLowerInvariant()));
        }

        /// <summary>
        /// Run a pre-flight test to verify the model can use tools correctly.
        /// Tests 3 simple tools with known expected results.
        /// </summary>
        /// <returns>True if the model passed the tools test, false otherwise</returns>
        private async Task<bool> RunToolsPreflightTestAsync(string modelName)
        {
            Log($"[dim]  Running tools pre-flight test...[/]");

            // Define 3 simple test cases with known results
            // Use explicit tool-calling language to help the model understand
            var testCases = new[]
            {
                // Order matters - we test different tools to verify tool calling capability
                ("Using the magic calculator, what is 5 + 3?", "magic_calculator", "9"),
                ("What is the average lifespan of a tiger?", "get_animal_lifespan", "15 years"),
                ("How many ingredients does apple pie need?", "get_recipe_ingredients", "8 ingredients"),
            };

            var tools = BenchTools.GetToolDefinitions(new List<string>
            {
                "magic_calculator",
                "get_animal_lifespan",
                "get_recipe_ingredients"
            });

            int passed = 0;
            int failed = 0;

            foreach (var (question, expectedTool, expectedResult) in testCases)
            {
                if (_cancellationRequested) break;

                try
                {
                    if (_args.Verbose)
                        Log($"    [dim]Testing: {Markup.Escape(question)}[/]");

                    var messages = new List<object>
                    {
                        new { role = "user", content = question }
                    };

                    // Use streaming to properly separate thinking from tool calls
                    var request = new
                    {
                        model = modelName,
                        messages = messages,
                        stream = true,
                        tools = tools,
                        options = new { num_ctx = 2048, num_predict = 2048 }
                    };

                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat");
                    httpRequest.Content = JsonContent.Create(request);
                    var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Log($"    [red]Pre-flight test failed: HTTP {response.StatusCode}[/]");
                        if (_args.Verbose)
                            Log($"    [dim]Error: {Markup.Escape(errorContent)}[/]");
                        failed++;
                        continue;
                    }

                    // Read streaming response and collect tool calls from final message
                    string? foundToolName = null;
                    string? foundToolArgs = null;
                    string responseContent = "";
                    string lastJson = "";

                    using var stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
                    using var reader = new StreamReader(stream);
                    while (!reader.EndOfStream && !timeoutCts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(timeoutCts.Token);
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        lastJson = line;
                        try
                        {
                            using var chunk = JsonDocument.Parse(line);
                            var root = chunk.RootElement;

                            // Check for tool_calls in any chunk
                            if (root.TryGetProperty("message", out var msg))
                            {
                                // Accumulate content
                                if (msg.TryGetProperty("content", out var contentElem))
                                {
                                    var content = contentElem.GetString();
                                    if (!string.IsNullOrEmpty(content))
                                        responseContent += content;
                                }

                                // Check for tool calls
                                if (msg.TryGetProperty("tool_calls", out var toolCalls))
                                {
                                    var toolCallsArray = toolCalls.EnumerateArray().ToList();
                                    if (toolCallsArray.Count > 0)
                                    {
                                        var firstCall = toolCallsArray[0];
                                        var function = firstCall.GetProperty("function");
                                        foundToolName = function.GetProperty("name").GetString();
                                        foundToolArgs = function.TryGetProperty("arguments", out var argsElem) ? argsElem.ToString() : "{}";
                                    }
                                }
                            }

                            // Check if done
                            if (root.TryGetProperty("done", out var doneElem) && doneElem.GetBoolean())
                                break;
                        }
                        catch (JsonException)
                        {
                            // Skip malformed chunks
                        }
                    }

                    // Evaluate result
                    if (!string.IsNullOrEmpty(foundToolName))
                    {
                        if (foundToolName.Equals(expectedTool, StringComparison.OrdinalIgnoreCase))
                        {
                            passed++;
                            Log($"    [green]✓ {expectedTool}[/]");
                            if (_args.Verbose)
                                Log($"      [dim]Args: {Markup.Escape(foundToolArgs ?? "{}")}[/]");
                        }
                        else
                        {
                            failed++;
                            Log($"    [yellow]✗ Expected {expectedTool}, got {foundToolName}[/]");
                            if (_args.Verbose)
                                Log($"      [dim]Args: {Markup.Escape(foundToolArgs ?? "{}")}[/]");
                        }
                    }
                    else
                    {
                        // Model didn't use tools
                        failed++;
                        Log($"    [yellow]✗ No tool call for {expectedTool} - model responded with text[/]");
                        if (_args.Verbose)
                        {
                            if (!string.IsNullOrEmpty(responseContent))
                            {
                                // Console: show truncated preview
                                var preview = responseContent.Length > 200 ? responseContent.Substring(0, 200) + "..." : responseContent;
                                AnsiConsole.MarkupLine($"      [dim]Response: {Markup.Escape(preview)}[/]");

                                // Log file: write full response
                                if (_logger != null)
                                {
                                    _logger.WriteLine("      Response:");
                                    foreach (var line in responseContent.Split('\n'))
                                    {
                                        _logger.WriteLine($"        {line}");
                                    }
                                }
                            }
                            else
                            {
                                // Console: show truncated preview
                                var jsonPreview = lastJson.Length > 500 ? lastJson.Substring(0, 500) + "..." : lastJson;
                                AnsiConsole.MarkupLine($"      [dim]Last chunk: {Markup.Escape(jsonPreview)}[/]");

                                // Log file: write full pretty-printed JSON
                                if (_logger != null && !string.IsNullOrEmpty(lastJson))
                                {
                                    try
                                    {
                                        using var doc = JsonDocument.Parse(lastJson);
                                        var prettyJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                                        _logger.WriteLine("      Last chunk:");
                                        foreach (var line in prettyJson.Split('\n'))
                                        {
                                            _logger.WriteLine($"        {line}");
                                        }
                                    }
                                    catch
                                    {
                                        // If JSON parsing fails, write raw
                                        _logger.WriteLine($"      Last chunk: {lastJson}");
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Log($"    [red]Pre-flight test error: {Markup.Escape(ex.Message)}[/]");
                }
            }

            var success = passed == 3; // All 3 tools must be used correctly
            if (success)
            {
                Log($"[green]  Pre-flight test passed ({passed}/3 tools used correctly)[/]");
            }
            else
            {
                Log($"[red]  Pre-flight test failed ({passed}/3 tools used correctly)[/]");
                Log($"[yellow]  Model does not support tools properly - cannot run ctxtoolsbench.[/]");
            }

            return success;
        }

        #endregion

        #region Category Testing

        private async Task RunCategoryTestsAsync(string modelName, BenchQuantResult quantResult)
        {
            // Build conversation context that accumulates across categories
            var conversationMessages = new List<BenchChatMessage>();
            var accumulatedContext = new StringBuilder();

            // Track cumulative suite tokens (user content only) for comparison with actual
            int cumulativeSuiteTokens = 0;

            // Initialize category results if not present
            if (quantResult.CategoryResults == null)
                quantResult.CategoryResults = new List<BenchCategoryResult>();

            // Reset context tracker for new model test
            _contextTracker?.ResetAll();

            // Note: Instructions are prepended to the first category's context + first question
            // We don't send instructions separately to avoid confusing the model

            for (int i = 0; i < _testSuite.Categories.Count; i++)
            {
                if (_cancellationRequested) break;

                var category = _testSuite.Categories[i];
                var isFirstCategory = i == 0;

                // Check if category already complete
                var existingCategoryResult = quantResult.CategoryResults
                    .FirstOrDefault(c => c.Category.Equals(category.Name, StringComparison.OrdinalIgnoreCase));

                if (existingCategoryResult != null && existingCategoryResult.IsComplete && !_args.Force && !_args.Rejudge)
                {
                    // CRITICAL FIX: Rebuild conversation messages for skipped categories
                    // so subsequent categories have proper context
                    accumulatedContext.AppendLine(category.Context);

                    // Add the category context message that was sent during original test
                    conversationMessages.Add(new BenchChatMessage
                    {
                        Role = "user",
                        Content = $"--- THE BOOK: CHRONICLES OF THE ANIMAL TRIBE ---\n\n{category.Context}"
                    });
                    // Add a placeholder acknowledgment (we don't have the original, but need to maintain structure)
                    conversationMessages.Add(new BenchChatMessage
                    {
                        Role = "assistant",
                        Content = "I have read and memorized the content."
                    });

                    Log($"[dim]  Skipping {category.Name} (already complete, rebuilding context)[/]");
                    continue;
                }

                Log($"[bold]  Category: {category.Name} ({category.ContextLength} tokens)[/]");

                // Initialize context tracker for this category
                // Track usage against category target, but only flag overflow if exceeding actual configured context
                _contextTracker = new ContextTracker(category.ContextLength, _effectiveMaxContext);

                // Initialize calibration for this category
                if (_args.Calibrate)
                {
                    InitializeCategoryCalibration(category);
                }

                // Build the context text to be prepended to the first question
                // For first category: Instructions + Context
                // For other categories: Just Context
                var contextMessageText = $"--- THE BOOK: CHRONICLES OF THE ANIMAL TRIBE ---\n\n{category.Context}";
                string contextForFirstQuestion;
                if (isFirstCategory && !string.IsNullOrWhiteSpace(_testSuite.Instructions))
                {
                    contextForFirstQuestion = $"{_testSuite.Instructions}\n\n{contextMessageText}";
                    var instructionsTokens = BenchTokenizer.EstimateTokenCount(_testSuite.Instructions);
                    cumulativeSuiteTokens += instructionsTokens;
                }
                else
                {
                    contextForFirstQuestion = contextMessageText;
                }

                // Calculate estimated tokens for this category's context
                var estContextTokens = BenchTokenizer.EstimateTokenCount(category.Context);
                var estMessageTokens = BenchTokenizer.EstimateTokenCount(contextMessageText);
                cumulativeSuiteTokens += estMessageTokens;

                // Track accumulated context for rebuilding on resume
                accumulatedContext.AppendLine(category.Context);

                if (_args.Verbose)
                {
                    Log($"    [dim]Context: {category.Context.Length} chars = ~{estContextTokens} tokens (message: ~{estMessageTokens} tokens)[/]");
                    Log($"    [dim]Accumulated messages: {conversationMessages.Count}, Cumulative suite tokens: ~{cumulativeSuiteTokens}[/]");
                    if (isFirstCategory && !string.IsNullOrWhiteSpace(_testSuite.Instructions))
                    {
                        Log($"    [dim]Instructions will be prepended to first question[/]");
                    }
                }

                // Create category result
                var categoryResult = existingCategoryResult ?? new BenchCategoryResult
                {
                    Category = category.Name,
                    TargetContextLength = category.ContextLength
                };

                if (existingCategoryResult == null)
                    quantResult.CategoryResults.Add(categoryResult);

                // Reset IsComplete when (re)processing a category - it will be set true only on full completion
                categoryResult.IsComplete = false;

                // Run questions for this category
                // Context is prepended to the first question of the category
                var contextUsedForCategory = false;

                if (isFirstCategory && category.Questions != null)
                {
                    // First category: direct questions (context + first question combined)
                    // Pass quantResult for parallel mode - queueing happens immediately inside RunQuestionsAsync
                    categoryResult.QuestionResults = await RunQuestionsAsync(
                        modelName, conversationMessages, category.Questions, category, null, contextForFirstQuestion, quantResult);
                    contextUsedForCategory = true;
                }
                else if (category.SubCategories != null)
                {
                    // Other categories: Old and New subcategories
                    categoryResult.SubCategoryResults = new List<BenchSubCategoryResult>();

                    foreach (var subCategory in category.SubCategories)
                    {
                        if (_cancellationRequested) break;

                        // Show subcategory progress
                        Log($"    [dim]Subcategory: {subCategory.Name} ({subCategory.Questions.Count} questions)[/]");

                        var subResult = new BenchSubCategoryResult
                        {
                            SubCategory = subCategory.Name
                        };

                        // Only prepend context to the first subcategory's first question
                        // Pass quantResult for parallel mode - queueing happens immediately inside RunQuestionsAsync
                        var contextForSubCategory = contextUsedForCategory ? null : contextForFirstQuestion;
                        subResult.QuestionResults = await RunQuestionsAsync(
                            modelName, conversationMessages, subCategory.Questions, category, subCategory, contextForSubCategory, quantResult);
                        contextUsedForCategory = true;

                        // Calculate subcategory scores
                        subResult.TotalQuestions = subResult.QuestionResults?.Count ?? 0;
                        subResult.CorrectAnswers = subResult.QuestionResults?.Count(q => q.Score >= 100) ?? 0;
                        subResult.Score = subResult.TotalQuestions > 0
                            ? (double)subResult.CorrectAnswers / subResult.TotalQuestions * 100
                            : 0;

                        // Show subcategory summary
                        if (_judgeEnabled)
                        {
                            Log($"    [cyan]{subCategory.Name}: {subResult.TotalQuestions} answers[/]");
                        }
                        else
                        {
                            var subScoreColor = subResult.Score >= 50 ? "green" : "yellow";
                            Log($"    [{subScoreColor}]{subCategory.Name}: {subResult.Score:F1}% ({subResult.CorrectAnswers}/{subResult.TotalQuestions})[/]");
                        }

                        categoryResult.SubCategoryResults.Add(subResult);
                    }
                }

                // Calculate category scores
                if (categoryResult.QuestionResults != null)
                {
                    categoryResult.TotalQuestions = categoryResult.QuestionResults.Count;
                    categoryResult.CorrectAnswers = categoryResult.QuestionResults.Count(q => q.Score >= 100);
                }
                else if (categoryResult.SubCategoryResults != null)
                {
                    categoryResult.TotalQuestions = categoryResult.SubCategoryResults.Sum(s => s.TotalQuestions);
                    categoryResult.CorrectAnswers = categoryResult.SubCategoryResults.Sum(s => s.CorrectAnswers);
                }

                categoryResult.Score = categoryResult.TotalQuestions > 0
                    ? (double)categoryResult.CorrectAnswers / categoryResult.TotalQuestions * 100
                    : 0;

                // Calculate category performance averages
                var allQuestionResults = GetAllQuestionResults(categoryResult);
                if (allQuestionResults.Count > 0)
                {
                    categoryResult.AvgResponseTimeMs = allQuestionResults.Average(q => q.ResponseTimeMs);
                    categoryResult.TotalPromptTokens = allQuestionResults.Sum(q => q.PromptTokens ?? 0);
                    categoryResult.TotalCompletionTokens = allQuestionResults.Sum(q => q.CompletionTokens ?? 0);

                    var promptToksSamples = allQuestionResults.Where(q => q.PromptToksPerSec.HasValue).Select(q => q.PromptToksPerSec!.Value).ToList();
                    var evalToksSamples = allQuestionResults.Where(q => q.EvalToksPerSec.HasValue).Select(q => q.EvalToksPerSec!.Value).ToList();

                    if (promptToksSamples.Count > 0)
                        categoryResult.AvgPromptToksPerSec = promptToksSamples.Average();
                    if (evalToksSamples.Count > 0)
                        categoryResult.AvgEvalToksPerSec = evalToksSamples.Average();
                }

                // Store context tracking data including thinking tokens
                if (_contextTracker != null)
                {
                    categoryResult.PeakContextTokens = _contextTracker.PeakTotalUsed;
                    categoryResult.ContextUsagePercent = _contextTracker.MaxContext > 0
                        ? (_contextTracker.PeakTotalUsed * 100.0 / _contextTracker.MaxContext)
                        : 0;
                    categoryResult.ContextOverflowed = _contextTracker.HasOverflowed;
                    categoryResult.TotalThinkingTokens = _contextTracker.CumulativeThinkingTokens;
                    categoryResult.PeakThinkingTokens = _contextTracker.PeakThinkingTokens;
                }

                // Only mark as complete if not cancelled during this category
                if (!_cancellationRequested)
                {
                    categoryResult.IsComplete = true;
                    // If judging is enabled, scores will be updated after judgment phase
                    if (_judgeEnabled)
                    {
                        Log($"    [cyan]Collected {categoryResult.TotalQuestions} answers (judgment pending)[/]");
                    }
                    else
                    {
                        Log($"    [green]Score: {categoryResult.Score:F1}% ({categoryResult.CorrectAnswers}/{categoryResult.TotalQuestions})[/]");
                    }
                    if (categoryResult.AvgPromptToksPerSec.HasValue || categoryResult.AvgEvalToksPerSec.HasValue)
                    {
                        Log($"    [dim]Avg performance: prompt={categoryResult.AvgPromptToksPerSec:F1} tok/s, eval={categoryResult.AvgEvalToksPerSec:F1} tok/s, time={categoryResult.AvgResponseTimeMs:F0}ms[/]");
                    }

                    // Show context tracking summary
                    if (_contextTracker != null)
                    {
                        var contextColor = _contextTracker.HasOverflowed ? "red" : (_contextTracker.UsagePercent > 90 ? "yellow" : "dim");
                        Log($"    [{contextColor}]{_contextTracker.GetPeakSummary()}[/]");
                    }
                }
                else
                {
                    Log($"    [yellow]Partial: {categoryResult.TotalQuestions} answers collected[/]");
                }

                // Finalize calibration for this category
                if (_args.Calibrate)
                {
                    FinalizeCategoryCalibration();
                }

                // Save progress after each category
                await SaveResultsFileAsync();
            }
        }

        private async Task<List<BenchQuestionResult>> RunQuestionsAsync(
            string modelName,
            List<BenchChatMessage> conversationMessages,
            List<BenchQuestion> questions,
            BenchCategory category,
            BenchSubCategory? subCategory,
            string? contextForFirstQuestion = null,
            BenchQuantResult? quantResult = null)
        {
            var results = new List<BenchQuestionResult>();
            var subCatLabel = subCategory != null ? $"/{subCategory.Name}" : "";
            var timingStats = new List<double>(); // Track response times in seconds
            var isFirstQuestion = true; // Track first question to prepend context
            var parallelMode = _judgeEnabled && IsParallelMode() && quantResult != null;

            if (_args.Verbose)
            {
                Log($"    [dim]Running {questions.Count} questions for {category.Name}{subCatLabel}...[/]");
                if (!string.IsNullOrEmpty(contextForFirstQuestion))
                {
                    Log($"    [dim]Context ({contextForFirstQuestion.Length} chars) will be prepended to first question[/]");
                }
            }

            // Track token speeds for progress display
            var promptSpeedSamples = new List<double>();
            var evalSpeedSamples = new List<double>();

            // Create suffix column for stats and model name (displayed after percentage)
            var suffixColumn = new ProgressSuffixColumn();

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    suffixColumn,
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    // Use total suite questions for overall progress tracking
                    var testTask = ctx.AddTask($"[cyan]{category.Name}{subCatLabel}[/] Q{_completedSuiteQuestions + 1}/{_totalSuiteQuestions}", maxValue: _totalSuiteQuestions);
                    testTask.Value = _completedSuiteQuestions;

                    // Add judgment progress bar for parallel mode (recreate each time with current state)
                    ProgressTask? judgeTask = null;
                    if (parallelMode)
                    {
                        var judgeStatsInfo = GetJudgmentSuffixInfo();
                        var judgeModelDisplay = _judgeModelName ?? "judge";
                        judgeTask = ctx.AddTask($"[magenta]Judging[/] {_judgmentCompletedCount}/{_judgmentQueuedCount}", maxValue: Math.Max(1, _totalSuiteQuestions));
                        judgeTask.Value = _judgmentCompletedCount;
                        _judgmentProgressTask = judgeTask; // Keep reference for consumer to update
                        // Set initial suffix for judge task
                        var initialJudgeSuffix = string.IsNullOrEmpty(judgeStatsInfo)
                            ? $"[dim]{judgeModelDisplay}[/]"
                            : $"({judgeStatsInfo} t/s) [dim]{judgeModelDisplay}[/]";
                        suffixColumn.SetSuffix(judgeTask, initialJudgeSuffix);
                    }

                    foreach (var question in questions)
                    {
                        if (_cancellationRequested)
                        {
                            testTask.Description = $"[yellow]Cancelled[/]";
                            break;
                        }

                        // Update description: Category/SubCat Q#/Total
                        testTask.Description = $"[cyan]{category.Name}{subCatLabel}[/] Q{_completedSuiteQuestions + 1}/{_totalSuiteQuestions}";

                        // Update suffix for test task: (timing stats + token speeds) model
                        var statsInfo = GetProgressStatsString(timingStats, promptSpeedSamples, evalSpeedSamples);
                        suffixColumn.SetSuffix(testTask, $"{statsInfo} [dim]{_currentModelTag}[/]");

                        // For the first question, prepend the context (instructions + chapter content)
                        var contextToUse = isFirstQuestion ? contextForFirstQuestion : null;
                        var result = await RunSingleQuestionAsync(modelName, conversationMessages, question, category, subCategory, contextToUse);
                        results.Add(result);
                        isFirstQuestion = false;

                        // In parallel mode, queue answer for judgment immediately
                        if (parallelMode && !string.IsNullOrEmpty(result.ModelAnswer) && !result.IsError)
                        {
                            QueueForJudgment(result, question, category, subCategory, quantResult!);
                        }

                        // Track timing
                        var responseTimeSec = result.ResponseTimeMs / 1000.0;
                        timingStats.Add(responseTimeSec);

                        // Track token speeds
                        if (result.PromptToksPerSec.HasValue && result.PromptToksPerSec > 0)
                            promptSpeedSamples.Add(result.PromptToksPerSec.Value);
                        if (result.EvalToksPerSec.HasValue && result.EvalToksPerSec > 0)
                            evalSpeedSamples.Add(result.EvalToksPerSec.Value);

                        // Update progress
                        _completedSuiteQuestions++;
                        testTask.Value = _completedSuiteQuestions;

                        // Update description and suffix after completion
                        testTask.Description = $"[cyan]{category.Name}{subCatLabel}[/] Q{_completedSuiteQuestions}/{_totalSuiteQuestions}";
                        statsInfo = GetProgressStatsString(timingStats, promptSpeedSamples, evalSpeedSamples);
                        suffixColumn.SetSuffix(testTask, $"{statsInfo} [dim]{_currentModelTag}[/]");

                        // Update judgment progress bar description and suffix with judge model name and metrics
                        if (judgeTask != null)
                        {
                            var judgeStatsInfo = GetJudgmentSuffixInfo();
                            var judgeModelDisplay = _judgeModelName ?? "judge";
                            judgeTask.Description = $"[magenta]Judging[/] {_judgmentCompletedCount}/{_judgmentQueuedCount}";
                            // Put judge metrics in suffix position (after progress bar)
                            var judgeSuffix = string.IsNullOrEmpty(judgeStatsInfo)
                                ? $"[dim]{judgeModelDisplay}[/]"
                                : $"({judgeStatsInfo} t/s) [dim]{judgeModelDisplay}[/]";
                            suffixColumn.SetSuffix(judgeTask, judgeSuffix);
                        }
                    }

                    // Final status
                    var finalStatsInfo = GetProgressStatsString(timingStats, promptSpeedSamples, evalSpeedSamples);
                    testTask.Description = $"[green]{category.Name}{subCatLabel}[/] Q{_completedSuiteQuestions}/{_totalSuiteQuestions}";
                    suffixColumn.SetSuffix(testTask, $"{finalStatsInfo} [dim]{_currentModelTag}[/]");
                });

            // Show detailed output in verbose mode (outside progress bar)
            if (_args.Verbose)
            {
                foreach (var result in results)
                {
                    var question = questions.First(q => q.Id == result.QuestionId);

                    // Calculate estimated test suite tokens for this Q&A
                    var estQuestionTokens = BenchTokenizer.EstimateTokenCount(question.Text);
                    var estRefAnswerTokens = BenchTokenizer.EstimateTokenCount(question.ReferenceAnswer);
                    var estSuiteTokens = estQuestionTokens + estRefAnswerTokens;

                    // Show full question/answer details
                    Log($"        [cyan]--- Question {question.Id} ---[/]");
                    Log($"        [dim]Q{question.Id} ({estQuestionTokens} est. tokens): {Markup.Escape(question.Text)}[/]");

                    // Show model thinking if present
                    if (!string.IsNullOrEmpty(result.ModelThinking))
                    {
                        Log($"        [magenta]Model Thinking ({result.ThinkingTokens ?? 0} tokens):[/]");
                        foreach (var line in result.ModelThinking.Split('\n'))
                        {
                            Log($"          [dim]{Markup.Escape(line)}[/]");
                        }
                    }

                    Log($"        [yellow]Model Response:[/]");
                    // Show full model answer, preserving newlines but escaping markup
                    foreach (var line in result.ModelAnswer.Split('\n'))
                    {
                        Log($"          [dim]{Markup.Escape(line)}[/]");
                    }

                    // Show reference answer AFTER model answer for comparison
                    Log($"        [green]Reference Answer ({estRefAnswerTokens} est. tokens): {Markup.Escape(question.ReferenceAnswer)}[/]");

                    // Show tool usage if any
                    if (result.ToolsUsed.Count > 0)
                    {
                        Log($"        [magenta]Tools Used:[/]");
                        foreach (var tool in result.ToolsUsed)
                        {
                            Log($"          [dim]Tool: {tool.ToolName}[/]");
                            if (!string.IsNullOrEmpty(tool.ArgumentsJson))
                            {
                                Log($"          [dim]Args: {Markup.Escape(tool.ArgumentsJson)}[/]");
                            }
                            Log($"          [dim]Result: {Markup.Escape(tool.Result ?? "null")}[/]");
                        }
                    }

                    // Show judge details if available
                    if (result.Judgment != null)
                    {
                        Log($"        [blue]Judge Verdict: {result.Judgment.Answer}[/]");
                        Log($"        [dim]Reason: {Markup.Escape(result.Judgment.Reason)}[/]");
                    }

                    // Show token comparison: estimated (test suite) vs actual
                    var actualModelTokens = result.CompletionTokens ?? 0;
                    var thinkingTokens = result.ThinkingTokens ?? 0;
                    Log($"        [dim]Suite tokens: Q={estQuestionTokens} + Ref={estRefAnswerTokens} = {estSuiteTokens} est.[/]");
                    var thinkingInfo = thinkingTokens > 0 ? $", thinking={thinkingTokens}" : "";
                    Log($"        [dim]Actual tokens: prompt={result.PromptTokens ?? 0}, completion={actualModelTokens}{thinkingInfo}[/]");
                    Log($"        [dim]Performance: prompt={result.PromptToksPerSec:F1} tok/s, eval={result.EvalToksPerSec:F1} tok/s[/]");

                    // Show context tracking
                    if (_contextTracker != null)
                    {
                        var contextColor = _contextTracker.HasOverflowed ? "red" : (_contextTracker.UsagePercent > 90 ? "yellow" : "dim");
                        Log($"        [{contextColor}]{_contextTracker.GetSummary()}[/]");
                    }

                    Log($"        [cyan]--- End Question {question.Id} ---[/]");
                    AnsiConsole.WriteLine();
                }
            }

            // In parallel mode with verbose, output any completed judgments from background processing
            // Use the same format as serial mode output
            if (_args.Verbose && parallelMode && _completedJudgments != null && !_completedJudgments.IsEmpty)
            {
                var judgmentsToLog = new List<CompletedJudgment>();
                // Peek at completed judgments without removing them (they'll be shown at the end)
                foreach (var j in _completedJudgments.ToArray())
                {
                    judgmentsToLog.Add(j);
                }

                if (judgmentsToLog.Count > 0)
                {
                    var newJudgments = judgmentsToLog.OrderBy(x => x.SequentialNumber).ToList();
                    Log($"\n    [magenta]--- Parallel Judgments Completed So Far ({newJudgments.Count}) ---[/]");
                    foreach (var j in newJudgments)
                    {
                        var subCatInfo = !string.IsNullOrEmpty(j.SubCategory) ? $"/{j.SubCategory}" : "";
                        var statusColor = j.IsCorrect ? "green" : "red";
                        var statusText = j.IsCorrect ? "CORRECT  " : "INCORRECT";
                        Log($"      Q{j.SequentialNumber}: [{statusColor}]{statusText}[/] ({Markup.Escape(j.Category)}{Markup.Escape(subCatInfo)})");
                    }
                    Log($"    [magenta]--- End Parallel Judgments ---[/]\n");
                }
            }

            return results;
        }

        /// <summary>
        /// Generate combined progress stats string (timing + token speeds) for progress display
        /// Format: (Avg: 4.2s Max: 8.1s p:2.5k e:115 t/s)
        /// </summary>
        private static string GetProgressStatsString(List<double> timingStats, List<double> promptSpeeds, List<double> evalSpeeds)
        {
            var parts = new List<string>();

            // Timing stats
            if (timingStats.Count > 0)
            {
                var avg = timingStats.Average();
                var max = timingStats.Max();
                parts.Add($"Avg:{avg:F1}s");
                parts.Add($"Max:{max:F1}s");
            }

            // Token speeds (integers, use k suffix only if >= 1000)
            if (promptSpeeds.Count > 0)
            {
                var avgPrompt = (int)Math.Ceiling(promptSpeeds.Average());
                var promptStr = avgPrompt >= 1000 ? $"{avgPrompt / 1000}k" : $"{avgPrompt}";
                parts.Add($"p:{promptStr}");
            }

            if (evalSpeeds.Count > 0)
            {
                var avgEval = (int)Math.Ceiling(evalSpeeds.Average());
                var evalStr = avgEval >= 1000 ? $"{avgEval / 1000}k" : $"{avgEval}";
                parts.Add($"e:{evalStr}");
            }

            // Add t/s suffix if we have token speeds
            if (promptSpeeds.Count > 0 || evalSpeeds.Count > 0)
            {
                parts.Add("t/s");
            }

            return parts.Count > 0 ? $"[dim]({string.Join(" ", parts)})[/]" : "";
        }

        private async Task<BenchQuestionResult> RunSingleQuestionAsync(
            string modelName,
            List<BenchChatMessage> conversationMessages,
            BenchQuestion question,
            BenchCategory category,
            BenchSubCategory? subCategory,
            string? contextToPrend = null)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = new BenchQuestionResult
            {
                QuestionId = question.Id,
                Question = question.Text,
                ReferenceAnswer = question.ReferenceAnswer,
                AboutCategory = question.AboutCategory
            };

            // Build question content - prepend context if provided (for first question of category)
            var questionContent = !string.IsNullOrEmpty(contextToPrend)
                ? $"{contextToPrend}\n\n{question.Text}"
                : question.Text;

            // Add question to conversation
            var questionMessage = new BenchChatMessage
            {
                Role = "user",
                Content = questionContent
            };

            var messagesWithQuestion = new List<BenchChatMessage>(conversationMessages) { questionMessage };

            // Retry loop for transient errors
            string? lastError = null;
            for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
            {
                if (_cancellationRequested) break;

                try
                {
                    // Send request (with tools if enabled)
                    // Use effective max context (limited by -L if specified, otherwise test suite max)
                    BenchChatResponse? response;
                    if (_testSuite.ToolsEnabled)
                    {
                        response = await SendChatWithToolsAsync(modelName, messagesWithQuestion, _effectiveMaxContext, question.ExpectedTools, result);
                    }
                    else
                    {
                        response = await SendChatMessageAsync(modelName, messagesWithQuestion, _effectiveMaxContext);
                    }

                    if (response != null)
                    {
                        result.ModelAnswer = response.Content;
                        result.ModelThinking = response.Thinking;
                        result.PromptTokens = response.PromptTokens;
                        result.CompletionTokens = response.CompletionTokens;
                        result.ThinkingTokens = response.ThinkingTokens;
                        result.PromptToksPerSec = response.PromptToksPerSec;
                        result.EvalToksPerSec = response.EvalToksPerSec;
                        result.TotalDurationMs = response.TotalDurationMs;
                        result.LoadDurationMs = response.LoadDurationMs;

                        // Update context tracker and store usage for this question
                        // Include thinking tokens in the context usage calculation
                        var totalOutputTokens = (response.CompletionTokens ?? 0) + (response.ThinkingTokens ?? 0);
                        _contextTracker?.UpdateFromResponse(response.PromptTokens, totalOutputTokens, response.ThinkingTokens);
                        result.ContextTokensUsed = (response.PromptTokens ?? 0) + totalOutputTokens;

                        // Token calibration tracking
                        if (_args.Calibrate && _currentCalibrationCategory != null)
                        {
                            var actualPrompt = response.PromptTokens ?? 0;
                            var actualOutput = response.CompletionTokens ?? 0;

                            // Track any previous assistant answer that's now part of the conversation context
                            // This is the answer from the previous question, which is included in this prompt
                            if (conversationMessages.Count > 0)
                            {
                                var lastMessage = conversationMessages.LastOrDefault();
                                if (lastMessage?.Role == "assistant" && !string.IsNullOrEmpty(lastMessage.Content))
                                {
                                    var answerNum = conversationMessages.Count(m => m.Role == "assistant");
                                    TrackCalibrationStep($"Answer{answerNum}", "answer", lastMessage.Content, actualPrompt, actualOutput);
                                }
                            }

                            // Track context if this is the first question (context is prepended)
                            // Separate instructions from context for better granularity
                            if (!string.IsNullOrEmpty(contextToPrend))
                            {
                                var instructions = _testSuite?.Instructions ?? "";
                                if (!string.IsNullOrEmpty(instructions) && contextToPrend.StartsWith(instructions))
                                {
                                    // Track instructions separately
                                    TrackCalibrationStep("Instructions", "instructions", instructions, actualPrompt, actualOutput);

                                    // Track remaining context (after instructions)
                                    var contextOnly = contextToPrend.Substring(instructions.Length).TrimStart('\n', '\r');
                                    if (!string.IsNullOrEmpty(contextOnly))
                                    {
                                        TrackCalibrationStep("Context", "context", contextOnly, actualPrompt, actualOutput);
                                    }
                                }
                                else
                                {
                                    // No separate instructions, track context as a whole
                                    TrackCalibrationStep("Context", "context", contextToPrend, actualPrompt, actualOutput);
                                }
                            }

                            // Track this question
                            TrackCalibrationStep($"Question{question.Id}", "question", question.Text, actualPrompt, actualOutput);
                        }

                        // Add Q&A to conversation for context continuity
                        conversationMessages.Add(questionMessage);
                        conversationMessages.Add(new BenchChatMessage
                        {
                            Role = "assistant",
                            Content = response.Content
                        });

                        stopwatch.Stop();
                        result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;

                        // Note: Judgment is deferred to after all questions are collected
                        // Score will be set to 0 now and updated after judgment phase
                        if (!_judgeEnabled)
                        {
                            // Simple string comparison as fallback when no judge
                            result.Score = result.ModelAnswer?.Contains(question.ReferenceAnswer, StringComparison.OrdinalIgnoreCase) == true ? 100 : 0;
                        }

                        // Success - return result
                        return result;
                    }
                    else
                    {
                        // Response was null - API error occurred, retry
                        lastError = "API returned no response";
                        if (attempt < MAX_RETRY_ATTEMPTS)
                        {
                            Log($"        [yellow]Retry {attempt}/{MAX_RETRY_ATTEMPTS}: {lastError}[/]");
                            await Task.Delay(RETRY_DELAY_MS);
                        }
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt < MAX_RETRY_ATTEMPTS)
                    {
                        Log($"        [yellow]Retry {attempt}/{MAX_RETRY_ATTEMPTS}: {lastError}[/]");
                        await Task.Delay(RETRY_DELAY_MS);
                    }
                }
            }

            // All retries exhausted - mark as fatal error
            // This will cause the entire test to abort (fail-fast behavior)
            stopwatch.Stop();
            result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
            result.ModelAnswer = $"Error: {lastError ?? "Unknown error"} (after {MAX_RETRY_ATTEMPTS} attempts)";
            result.Score = 0;
            result.IsError = true;

            // Signal fatal error - test should abort immediately
            Log($"[red]  Fatal API error on Q{question.Id}: {lastError}[/]");
            throw new BenchFatalErrorException($"API error after {MAX_RETRY_ATTEMPTS} retries: {lastError}");
        }

        /// <summary>
        /// Judge all answers after all questions have been collected
        /// </summary>
        private async Task JudgeAllAnswersAsync(BenchQuantResult quantResult)
        {
            var allQuestions = new List<(BenchQuestionResult Result, BenchQuestion Question, BenchCategory Category, BenchSubCategory? SubCategory)>();

            // Collect all questions that need judgment
            foreach (var categoryResult in quantResult.CategoryResults)
            {
                var category = _testSuite.Categories.FirstOrDefault(c => c.Name == categoryResult.Category);
                if (category == null) continue;

                if (categoryResult.QuestionResults != null)
                {
                    foreach (var qr in categoryResult.QuestionResults)
                    {
                        var question = category.Questions?.FirstOrDefault(q => q.Id == qr.QuestionId);
                        // Skip questions with errors or empty answers; include already-judged if --rejudge
                        if (question != null && !string.IsNullOrEmpty(qr.ModelAnswer) && (qr.Judgment == null || _args.Rejudge) && !qr.IsError)
                        {
                            allQuestions.Add((qr, question, category, null));
                        }
                    }
                }

                if (categoryResult.SubCategoryResults != null)
                {
                    foreach (var subResult in categoryResult.SubCategoryResults)
                    {
                        var subCategory = category.SubCategories?.FirstOrDefault(s => s.Name == subResult.SubCategory);
                        if (subCategory == null) continue;

                        foreach (var qr in subResult.QuestionResults)
                        {
                            var question = subCategory.Questions.FirstOrDefault(q => q.Id == qr.QuestionId);
                            // Skip questions with errors or empty answers; include already-judged if --rejudge
                            if (question != null && !string.IsNullOrEmpty(qr.ModelAnswer) && (qr.Judgment == null || _args.Rejudge) && !qr.IsError)
                            {
                                allQuestions.Add((qr, question, category, subCategory));
                            }
                        }
                    }
                }
            }

            var totalToJudge = allQuestions.Count;
            Log($"    [dim]Judging {totalToJudge} answers...[/]");

            if (totalToJudge == 0)
            {
                Log($"    [yellow]No answers to judge[/]");
                return;
            }

            // Track question counts per category/subcategory for detailed output
            var subCatCounts = new Dictionary<string, (int current, int total)>();
            var categoryCounts = new Dictionary<string, int>();
            foreach (var (_, _, cat, subCat) in allQuestions)
            {
                var subKey = $"{cat.Name}/{subCat?.Name ?? ""}";
                if (!subCatCounts.ContainsKey(subKey))
                    subCatCounts[subKey] = (0, 0);
                subCatCounts[subKey] = (subCatCounts[subKey].current, subCatCounts[subKey].total + 1);

                if (!categoryCounts.ContainsKey(cat.Name))
                    categoryCounts[cat.Name] = 0;
                categoryCounts[cat.Name]++;
            }

            // Calculate widths for formatting
            var totalWidth = totalToJudge.ToString().Length;
            var maxSubCatTotal = subCatCounts.Values.Max(x => x.total);
            var subCatTotalWidth = maxSubCatTotal.ToString().Length;
            var maxCatTotal = categoryCounts.Values.Max();
            var catTotalWidth = maxCatTotal.ToString().Length;

            int judged = 0;
            foreach (var (result, question, category, subCategory) in allQuestions)
            {
                if (_cancellationRequested) break;

                var judgeResult = await JudgeAnswerAsync(question, result.ModelAnswer, category, subCategory);
                result.Judgment = judgeResult?.Judgment;
                result.Score = judgeResult?.Judgment?.Answer?.Equals("YES", StringComparison.OrdinalIgnoreCase) == true ? 100 : 0;

                judged++;

                // Update and get subcategory question number
                var subCatKey = $"{category.Name}/{subCategory?.Name ?? ""}";
                var (current, total) = subCatCounts[subCatKey];
                current++;
                subCatCounts[subCatKey] = (current, total);
                var categoryTotal = categoryCounts[category.Name];

                var scoreColor = result.Score >= 100 ? "green" : "red";
                var resultText = result.Score >= 100 ? "CORRECT  " : "INCORRECT";
                var subCatLabel = subCategory != null ? "/" + subCategory.Name : "";
                // Format with proper alignment: Q#/Total: RESULT (Category/SubCat:QuestionInSubCat/SubCatTotal/CategoryTotal)
                var qNum = $"Q{judged}".PadLeft(totalWidth + 1);
                var subCatNum = current.ToString().PadLeft(subCatTotalWidth);
                var subCatTotalStr = total.ToString().PadLeft(subCatTotalWidth);
                var catTotalStr = categoryTotal.ToString().PadLeft(catTotalWidth);
                Log($"    [{scoreColor}]{qNum}/{totalToJudge}: {resultText}[/] [dim]({category.Name}{subCatLabel}: {subCatNum}/{subCatTotalStr}/{catTotalStr})[/]");

                // Update category scores as we go
                UpdateCategoryScores(quantResult);
            }

            // Save after all judgments
            await SaveResultsFileAsync();
            Log($"    [dim]Judged {judged} answers[/]");
        }

        /// <summary>
        /// Update all category scores based on current question results
        /// </summary>
        private void UpdateCategoryScores(BenchQuantResult quantResult)
        {
            foreach (var categoryResult in quantResult.CategoryResults)
            {
                if (categoryResult.QuestionResults != null)
                {
                    categoryResult.TotalQuestions = categoryResult.QuestionResults.Count;
                    categoryResult.CorrectAnswers = categoryResult.QuestionResults.Count(q => q.Score >= 100);
                }
                else if (categoryResult.SubCategoryResults != null)
                {
                    foreach (var subResult in categoryResult.SubCategoryResults)
                    {
                        subResult.TotalQuestions = subResult.QuestionResults.Count;
                        subResult.CorrectAnswers = subResult.QuestionResults.Count(q => q.Score >= 100);
                        subResult.Score = subResult.TotalQuestions > 0
                            ? (double)subResult.CorrectAnswers / subResult.TotalQuestions * 100
                            : 0;
                    }
                    categoryResult.TotalQuestions = categoryResult.SubCategoryResults.Sum(s => s.TotalQuestions);
                    categoryResult.CorrectAnswers = categoryResult.SubCategoryResults.Sum(s => s.CorrectAnswers);
                }

                categoryResult.Score = categoryResult.TotalQuestions > 0
                    ? (double)categoryResult.CorrectAnswers / categoryResult.TotalQuestions * 100
                    : 0;
            }
        }

        /// <summary>
        /// Start the background judgment consumer task for parallel mode
        /// </summary>
        private void StartJudgmentConsumer()
        {
            _judgmentQueue = new ConcurrentQueue<JudgmentQueueItem>();
            _completedJudgments = new ConcurrentQueue<CompletedJudgment>();
            _judgmentQueuedCount = 0;
            _judgmentCompletedCount = 0;
            _judgmentQueueComplete = false;
            _judgmentProgressTask = null; // Reset for new model
            _parallelJudgmentSequence = 0;
            _parallelSubCatCounts = new Dictionary<string, (int current, int total)>();
            _parallelCategoryCounts = new Dictionary<string, int>();
            _judgmentTimings = new List<double>();
            _judgmentPromptSpeeds = new List<double>();
            _judgmentEvalSpeeds = new List<double>();
            _judgmentSuffixInfo = "";

            _judgmentConsumerTask = Task.Run(async () =>
            {
                while (!_judgmentQueueComplete || !_judgmentQueue.IsEmpty)
                {
                    if (_cancellationRequested) break;

                    if (_judgmentQueue.TryDequeue(out var item))
                    {
                        try
                        {
                            var sw = Stopwatch.StartNew();
                            var judgeResult = await JudgeAnswerAsync(item.Question, item.Result.ModelAnswer, item.Category, item.SubCategory);
                            sw.Stop();
                            var judgment = judgeResult?.Judgment;
                            item.Result.Judgment = judgment;
                            var isCorrect = judgment?.Answer?.Equals("YES", StringComparison.OrdinalIgnoreCase) == true;
                            item.Result.Score = isCorrect ? 100 : 0;
                            UpdateCategoryScores(item.QuantResult);

                            // Track timing and performance metrics (thread-safe)
                            var timeSec = sw.Elapsed.TotalSeconds;
                            lock (_judgmentTimings!)
                            {
                                _judgmentTimings.Add(timeSec);
                                // Track prompt/eval speeds if available
                                if (judgeResult != null && judgeResult.PromptToksPerSec > 0)
                                    _judgmentPromptSpeeds!.Add(judgeResult.PromptToksPerSec);
                                if (judgeResult != null && judgeResult.EvalToksPerSec > 0)
                                    _judgmentEvalSpeeds!.Add(judgeResult.EvalToksPerSec);
                                UpdateJudgmentSuffixInfo();
                            }

                            // Get tracking info (thread-safe increment)
                            var seqNum = Interlocked.Increment(ref _parallelJudgmentSequence);
                            var subCatKey = $"{item.Category.Name}/{item.SubCategory?.Name ?? ""}";
                            int subCatCurrent = 0, subCatTotal = 0, catTotal = 0;
                            lock (_parallelSubCatCounts!)
                            {
                                if (_parallelSubCatCounts.TryGetValue(subCatKey, out var sc))
                                {
                                    subCatCurrent = sc.current + 1;
                                    subCatTotal = sc.total;
                                    _parallelSubCatCounts[subCatKey] = (subCatCurrent, subCatTotal);
                                }
                            }
                            lock (_parallelCategoryCounts!)
                            {
                                catTotal = _parallelCategoryCounts.GetValueOrDefault(item.Category.Name, 0);
                            }

                            // Store completed judgment for verbose output
                            _completedJudgments?.Enqueue(new CompletedJudgment
                            {
                                QuestionId = item.Question.Id,
                                Category = item.Category.Name,
                                SubCategory = item.SubCategory?.Name,
                                Answer = judgment?.Answer ?? "ERROR",
                                Reason = judgment?.Reason ?? "Judgment failed",
                                IsCorrect = isCorrect,
                                SequentialNumber = seqNum,
                                SubCatQuestionNumber = subCatCurrent,
                                SubCatTotal = subCatTotal,
                                CategoryTotal = catTotal
                            });

                            // Update progress bar if available
                            _judgmentProgressTask?.Increment(1);
                        }
                        catch (Exception ex)
                        {
                            var seqNum = Interlocked.Increment(ref _parallelJudgmentSequence);
                            // Store error for verbose output
                            _completedJudgments?.Enqueue(new CompletedJudgment
                            {
                                QuestionId = item.Question.Id,
                                Category = item.Category.Name,
                                SubCategory = item.SubCategory?.Name,
                                Answer = "ERROR",
                                Reason = ex.Message,
                                IsCorrect = false,
                                SequentialNumber = seqNum
                            });
                            _judgmentProgressTask?.Increment(1);
                        }
                        Interlocked.Increment(ref _judgmentCompletedCount);
                    }
                    else
                    {
                        // Wait a bit before checking again
                        await Task.Delay(50);
                    }
                }
            });
        }

        /// <summary>
        /// Queue an answer for parallel judgment
        /// </summary>
        private void QueueForJudgment(BenchQuestionResult result, BenchQuestion question, BenchCategory category, BenchSubCategory? subCategory, BenchQuantResult quantResult)
        {
            if (_judgmentQueue == null) return;

            // Track totals for output formatting
            var subCatKey = $"{category.Name}/{subCategory?.Name ?? ""}";
            lock (_parallelSubCatCounts!)
            {
                if (!_parallelSubCatCounts.ContainsKey(subCatKey))
                    _parallelSubCatCounts[subCatKey] = (0, 0);
                var (current, total) = _parallelSubCatCounts[subCatKey];
                _parallelSubCatCounts[subCatKey] = (current, total + 1);
            }
            lock (_parallelCategoryCounts!)
            {
                if (!_parallelCategoryCounts.ContainsKey(category.Name))
                    _parallelCategoryCounts[category.Name] = 0;
                _parallelCategoryCounts[category.Name]++;
            }

            _judgmentQueue.Enqueue(new JudgmentQueueItem
            {
                Result = result,
                Question = question,
                Category = category,
                SubCategory = subCategory,
                QuantResult = quantResult
            });
            Interlocked.Increment(ref _judgmentQueuedCount);
        }

        /// <summary>
        /// Wait for all queued judgments to complete and show results
        /// </summary>
        private async Task WaitForPendingJudgmentsAsync()
        {
            if (_judgmentConsumerTask == null) return;

            _judgmentQueueComplete = true;

            var pendingCount = _judgmentQueuedCount - _judgmentCompletedCount;
            if (pendingCount <= 0)
            {
                // All judgments already complete - show any remaining results
                ShowPendingJudgmentResults();
                Log($"[green]✓ All background judgments already complete[/]");
                ResetJudgmentTracking();
                return;
            }

            Log($"\n[magenta]Waiting for {pendingCount} background judgment(s) to complete...[/]");

            // Show progress bar while waiting
            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                })
                .StartAsync(async ctx =>
                {
                    var task = ctx.AddTask($"[magenta]Finishing judgments[/] {_judgmentCompletedCount}/{_judgmentQueuedCount}", maxValue: _judgmentQueuedCount);
                    task.Value = _judgmentCompletedCount;

                    while (_judgmentCompletedCount < _judgmentQueuedCount && !_cancellationRequested)
                    {
                        await Task.Delay(200);
                        task.Value = _judgmentCompletedCount;
                        task.Description = $"[magenta]Finishing judgments[/] {_judgmentCompletedCount}/{_judgmentQueuedCount}";
                    }

                    task.Value = _judgmentQueuedCount;
                    task.Description = $"[green]Judgments complete[/] {_judgmentCompletedCount}/{_judgmentQueuedCount}";
                });

            await _judgmentConsumerTask;

            // Show judgment results
            ShowPendingJudgmentResults();

            Log($"[green]✓ All background judgments complete ({_judgmentCompletedCount}/{_judgmentQueuedCount})[/]");
            ResetJudgmentTracking();
        }

        /// <summary>
        /// Show any pending judgment results from the completed queue
        /// Matches the serial mode output format
        /// </summary>
        private void ShowPendingJudgmentResults()
        {
            if (_completedJudgments == null) return;

            var results = new List<CompletedJudgment>();
            while (_completedJudgments.TryDequeue(out var completed))
            {
                results.Add(completed);
            }

            if (results.Count == 0) return;

            var totalJudged = results.Count;
            Log($"\n[bold]  Judging answers...[/]");
            Log($"    [dim]Judging {totalJudged} answers...[/]");

            // Sort by sequential number to maintain order
            var sortedResults = results.OrderBy(x => x.SequentialNumber).ToList();

            // Recalculate subcategory and category totals from the results (final totals)
            var subCatTotals = new Dictionary<string, int>();
            var categoryTotals = new Dictionary<string, int>();
            foreach (var j in sortedResults)
            {
                var subCatKey = $"{j.Category}/{j.SubCategory ?? ""}";
                if (!subCatTotals.ContainsKey(subCatKey))
                    subCatTotals[subCatKey] = 0;
                subCatTotals[subCatKey]++;

                if (!categoryTotals.ContainsKey(j.Category))
                    categoryTotals[j.Category] = 0;
                categoryTotals[j.Category]++;
            }

            // Calculate widths for formatting (matching serial mode)
            var totalWidth = totalJudged.ToString().Length;
            var maxSubCatTotal = subCatTotals.Values.Max();
            var subCatTotalWidth = maxSubCatTotal.ToString().Length;
            var maxCatTotal = categoryTotals.Values.Max();
            var catTotalWidth = maxCatTotal.ToString().Length;

            // Track current position within each subcategory for output
            var subCatCurrentPos = new Dictionary<string, int>();

            int displayed = 0;
            foreach (var j in sortedResults)
            {
                displayed++;
                var statusColor = j.IsCorrect ? "green" : "red";
                var resultText = j.IsCorrect ? "CORRECT  " : "INCORRECT";
                var subCatLabel = !string.IsNullOrEmpty(j.SubCategory) ? "/" + j.SubCategory : "";

                // Calculate current position in subcategory
                var subCatKey = $"{j.Category}/{j.SubCategory ?? ""}";
                if (!subCatCurrentPos.ContainsKey(subCatKey))
                    subCatCurrentPos[subCatKey] = 0;
                subCatCurrentPos[subCatKey]++;
                var posInSubCat = subCatCurrentPos[subCatKey];
                var subCatTotal = subCatTotals[subCatKey];
                var catTotal = categoryTotals[j.Category];

                // Format with proper alignment: Q#/Total: RESULT (Category/SubCat:QuestionInSubCat/SubCatTotal/CategoryTotal)
                var qNum = $"Q{displayed}".PadLeft(totalWidth + 1);
                var subCatNum = posInSubCat.ToString().PadLeft(subCatTotalWidth);
                var subCatTotalStr = subCatTotal.ToString().PadLeft(subCatTotalWidth);
                var catTotalStr = catTotal.ToString().PadLeft(catTotalWidth);

                Log($"    [{statusColor}]{qNum}/{totalJudged}: {resultText}[/] [dim]({Markup.Escape(j.Category)}{Markup.Escape(subCatLabel)}: {subCatNum}/{subCatTotalStr}/{catTotalStr})[/]");
            }

            Log($"    [dim]Judged {totalJudged} answers[/]");
        }

        /// <summary>
        /// Reset judgment tracking state for the next model
        /// </summary>
        private void ResetJudgmentTracking()
        {
            _judgmentProgressTask = null;
            _completedJudgments = null;
        }

        /// <summary>
        /// Update the judgment suffix info string based on current metrics
        /// Called from within a lock on _judgmentTimings
        /// </summary>
        private void UpdateJudgmentSuffixInfo()
        {
            if (_judgmentTimings == null || _judgmentTimings.Count == 0)
            {
                _judgmentSuffixInfo = "";
                return;
            }

            var parts = new List<string>();
            var avg = _judgmentTimings.Average();
            var max = _judgmentTimings.Max();
            parts.Add($"Avg:{avg:F1}s");
            parts.Add($"Max:{max:F1}s");

            // Add prompt/eval speeds if available
            if (_judgmentPromptSpeeds != null && _judgmentPromptSpeeds.Count > 0)
            {
                var avgPrompt = _judgmentPromptSpeeds.Average();
                parts.Add($"p:{avgPrompt / 1000:F0}k");
            }
            if (_judgmentEvalSpeeds != null && _judgmentEvalSpeeds.Count > 0)
            {
                var avgEval = _judgmentEvalSpeeds.Average();
                parts.Add($"e:{avgEval:F0}");
            }

            _judgmentSuffixInfo = string.Join(" ", parts);
        }

        /// <summary>
        /// Get the current judgment suffix info string
        /// </summary>
        private string GetJudgmentSuffixInfo()
        {
            return _judgmentSuffixInfo ?? "";
        }

        /// <summary>
        /// Load a model into Ollama memory
        /// </summary>
        private async Task LoadModelAsync(string modelName, HttpClient? client = null, string? baseUrl = null)
        {
            var httpClient = client ?? _httpClient;
            var url = baseUrl ?? _baseUrl;
            try
            {
                var response = await httpClient.PostAsJsonAsync($"{url}/api/generate", new
                {
                    model = modelName,
                    prompt = "",
                    keep_alive = "30m"
                });
                response.EnsureSuccessStatusCode();
                // Consume the streaming response to ensure the request completes
                await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Could not preload model {modelName}: {ex.Message}[/]");
            }
        }

        /// <summary>
        /// Unload a model from Ollama memory and wait until it's fully unloaded
        /// </summary>
        private async Task UnloadModelAsync(string modelName, HttpClient? client = null, string? baseUrl = null)
        {
            var httpClient = client ?? _httpClient;
            var url = baseUrl ?? _baseUrl;
            try
            {
                // Send unload request and consume response to ensure it's processed
                var response = await httpClient.PostAsJsonAsync($"{url}/api/generate", new
                {
                    model = modelName,
                    prompt = "",
                    keep_alive = "0"
                });
                response.EnsureSuccessStatusCode();
                await response.Content.ReadAsStringAsync();

                // Normalize model name for comparison (remove :latest if present)
                var normalizedName = modelName.ToLowerInvariant();
                if (normalizedName.EndsWith(":latest"))
                    normalizedName = normalizedName[..^7];

                // Poll /api/ps until model is unloaded (max 30 seconds)
                var maxWait = TimeSpan.FromSeconds(30);
                var started = DateTime.UtcNow;
                int pollCount = 0;
                while (DateTime.UtcNow - started < maxWait)
                {
                    var psResponse = await httpClient.GetFromJsonAsync<OllamaPsResponse>($"{url}/api/ps");

                    // Check if any loaded model matches our target
                    bool modelStillLoaded = false;
                    if (psResponse?.models != null)
                    {
                        foreach (var m in psResponse.models)
                        {
                            var loadedName = m.name.ToLowerInvariant();
                            if (loadedName.EndsWith(":latest"))
                                loadedName = loadedName[..^7];

                            if (loadedName == normalizedName || loadedName.StartsWith(normalizedName + ":"))
                            {
                                modelStillLoaded = true;
                                if (_args.Verbose && pollCount == 0)
                                    Log($"[dim]    Waiting for {m.name} to unload...[/]");
                                break;
                            }
                        }
                    }

                    if (!modelStillLoaded)
                    {
                        // Model is unloaded, add a small delay for VRAM to be freed
                        await Task.Delay(1000);
                        return;
                    }

                    pollCount++;
                    await Task.Delay(500); // Check every 500ms
                }
                Log($"[yellow]Warning: Model {modelName} may not have fully unloaded after 30s[/]");
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Could not unload model {modelName}: {ex.Message}[/]");
            }
        }

        /// <summary>
        /// Get list of currently loaded model names
        /// </summary>
        private async Task<List<string>> GetLoadedModelsAsync()
        {
            try
            {
                var psResponse = await _httpClient.GetFromJsonAsync<OllamaPsResponse>($"{_baseUrl}/api/ps");
                if (psResponse?.models == null || psResponse.models.Count == 0)
                {
                    return new List<string>();
                }
                return psResponse.models.Select(m => m.name).ToList();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Unload all models currently loaded on the Ollama instance
        /// </summary>
        private async Task UnloadAllModelsAsync()
        {
            try
            {
                var psResponse = await _httpClient.GetFromJsonAsync<OllamaPsResponse>($"{_baseUrl}/api/ps");
                if (psResponse?.models == null || psResponse.models.Count == 0)
                {
                    if (_args.Verbose)
                        Log($"[dim]No models loaded[/]");
                    return;
                }

                Log($"[dim]Unloading {psResponse.models.Count} loaded model(s)...[/]");

                foreach (var model in psResponse.models)
                {
                    if (_args.Verbose)
                        Log($"[dim]  Unloading {model.name}...[/]");
                    await UnloadModelAsync(model.name);
                }

                Log($"[dim]All models unloaded[/]");
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Could not unload all models: {ex.Message}[/]");
            }
        }

        private class OllamaPsResponse
        {
            public List<OllamaPsModel>? models { get; set; }
        }

        private class OllamaPsModel
        {
            public string name { get; set; } = "";
            public string model { get; set; } = "";
            public long size { get; set; }
        }

        #endregion

        #region Chat API

        private class BenchChatMessage
        {
            public string Role { get; set; } = "";
            public string Content { get; set; } = "";
            public List<BenchToolCall>? ToolCalls { get; set; }
        }

        private class BenchToolCall
        {
            public string Name { get; set; } = "";
            public Dictionary<string, JsonElement>? Arguments { get; set; }
        }

        private class BenchChatResponse
        {
            public string Content { get; set; } = "";
            public string? Thinking { get; set; }
            public int? PromptTokens { get; set; }
            public int? CompletionTokens { get; set; }
            public int? ThinkingTokens { get; set; }
            public List<BenchToolCall>? ToolCalls { get; set; }

            // Performance metrics from Ollama API
            public long? TotalDurationNs { get; set; }
            public long? LoadDurationNs { get; set; }
            public long? PromptEvalDurationNs { get; set; }
            public long? EvalDurationNs { get; set; }

            // Calculated metrics
            public double? PromptToksPerSec => PromptTokens.HasValue && PromptEvalDurationNs.HasValue && PromptEvalDurationNs > 0
                ? PromptTokens.Value / (PromptEvalDurationNs.Value / 1_000_000_000.0)
                : null;

            public double? EvalToksPerSec => CompletionTokens.HasValue && EvalDurationNs.HasValue && EvalDurationNs > 0
                ? CompletionTokens.Value / (EvalDurationNs.Value / 1_000_000_000.0)
                : null;

            public long? TotalDurationMs => TotalDurationNs.HasValue ? TotalDurationNs.Value / 1_000_000 : null;
            public long? LoadDurationMs => LoadDurationNs.HasValue ? LoadDurationNs.Value / 1_000_000 : null;
        }

        private async Task<BenchChatResponse?> SendChatMessageAsync(
            string modelName,
            List<BenchChatMessage> messages,
            int contextLength)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var options = BuildOptions(contextLength);
                var messagesList = messages.Select(m => new { role = m.Role, content = m.Content }).ToList();
                var systemPrompt = _testSuite?.SystemPrompt;

                // Build request with think parameter at top level (not inside options)
                // Think parameter logic:
                // - If ThinkLevel is set: use that level (string like "low", "medium", "high") - overrides EnableThinking
                // - Else if EnableThinking: think = true
                // - Else: think = false (default, thinking disabled)
                // Note: For some models (qwen3), think=false embeds thinking in content - fallback parsing handles this
                object thinkValue = !string.IsNullOrWhiteSpace(_args.ThinkLevel) ? _args.ThinkLevel : _args.EnableThinking;

                object request;
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                {
                    request = new
                    {
                        model = modelName,
                        messages = messagesList,
                        stream = false,
                        think = thinkValue,
                        system = systemPrompt,
                        options
                    };
                }
                else
                {
                    request = new
                    {
                        model = modelName,
                        messages = messagesList,
                        stream = false,
                        think = thinkValue,
                        options
                    };
                }

                if (_args.Verbose)
                {
                    var totalChars = messages.Sum(m => m.Content.Length);
                    Log($"    [cyan]--- Chat Request ---[/]");
                    Log($"    [dim]Model: {modelName}, Messages: {messages.Count}, Chars: ~{totalChars}, Context: {contextLength}[/]");

                    // Show system prompt if present
                    if (!string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        Log($"    [magenta][[SYSTEM]][/]");
                        foreach (var line in systemPrompt.Split('\n'))
                        {
                            Log($"      [dim]{Markup.Escape(line)}[/]");
                        }
                    }

                    // Show full messages
                    foreach (var m in messages)
                    {
                        Log($"    [yellow][[{m.Role.ToUpper()}]][/]");
                        foreach (var line in m.Content.Split('\n'))
                        {
                            Log($"      [dim]{Markup.Escape(line)}[/]");
                        }
                    }
                    Log($"    [cyan]--- End Request ---[/]");
                }

                // Use per-request timeout via CancellationTokenSource
                // Link with main cancellation token for immediate cancellation during testing
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_args.Timeout));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", request, linkedCts.Token);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                sw.Stop();
                if (_args.Verbose)
                {
                    Log($"    [dim]Chat response received in {sw.ElapsedMilliseconds}ms[/]");
                }

                // Extract message content and thinking
                string content = "";
                string? thinking = null;
                if (root.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("content", out var c))
                        content = c.GetString() ?? "";
                    if (msg.TryGetProperty("thinking", out var t))
                        thinking = t.GetString();
                }

                // Fallback: if thinking is embedded in content (ends with </think>\n\nAnswer pattern), extract it
                // This can happen if think=false was somehow used and the model embeds thinking in content
                if (string.IsNullOrEmpty(thinking) && content.Contains("</think>"))
                {
                    var thinkEndIndex = content.IndexOf("</think>", StringComparison.Ordinal);
                    if (thinkEndIndex > 0)
                    {
                        thinking = content.Substring(0, thinkEndIndex).Trim();
                        content = content.Substring(thinkEndIndex + "</think>".Length).Trim();
                    }
                }

                var chatResponse = new BenchChatResponse
                {
                    Content = content,
                    Thinking = !string.IsNullOrEmpty(thinking) ? thinking : null,
                    PromptTokens = root.TryGetProperty("prompt_eval_count", out var pt) ? pt.GetInt32() : null,
                    CompletionTokens = root.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : null,
                    TotalDurationNs = root.TryGetProperty("total_duration", out var td) ? td.GetInt64() : null,
                    LoadDurationNs = root.TryGetProperty("load_duration", out var ld) ? ld.GetInt64() : null,
                    PromptEvalDurationNs = root.TryGetProperty("prompt_eval_duration", out var ped) ? ped.GetInt64() : null,
                    EvalDurationNs = root.TryGetProperty("eval_duration", out var ed) ? ed.GetInt64() : null
                };

                // Estimate thinking tokens if thinking is present (for context tracking, regardless of storage)
                if (!string.IsNullOrEmpty(thinking))
                {
                    chatResponse.ThinkingTokens = BenchTokenizer.EstimateTokenCount(thinking);
                }

                if (_args.Verbose)
                {
                    Log($"    [dim]Performance: prompt={chatResponse.PromptToksPerSec:F1} tok/s, eval={chatResponse.EvalToksPerSec:F1} tok/s, total={chatResponse.TotalDurationMs}ms[/]");
                }

                return chatResponse;
            }
            catch (Exception ex)
            {
                sw.Stop();
                if (_args.Verbose)
                    Log($"[red]Chat error after {sw.ElapsedMilliseconds}ms: {ex.Message}[/]");
                return null;
            }
        }

        private async Task<BenchChatResponse?> SendChatWithToolsAsync(
            string modelName,
            List<BenchChatMessage> messages,
            int contextLength,
            List<string>? expectedTools,
            BenchQuestionResult result)
        {
            var maxIterations = 5; // Prevent infinite tool loops
            var currentMessages = new List<object>();

            // Convert messages to API format
            foreach (var m in messages)
            {
                currentMessages.Add(new { role = m.Role, content = m.Content });
            }

            var tools = _testSuite.EnabledTools != null
                ? BenchTools.GetToolDefinitions(_testSuite.EnabledTools)
                : BenchTools.GetAllToolDefinitions();

            var systemPrompt = _testSuite?.SystemPrompt;

            for (int iteration = 0; iteration < maxIterations; iteration++)
            {
                try
                {
                    var requestOptions = BuildOptions(contextLength);

                    // Build request with think parameter at top level
                    // Think parameter logic: ThinkLevel overrides EnableThinking
                    object thinkValue = !string.IsNullOrWhiteSpace(_args.ThinkLevel) ? _args.ThinkLevel : _args.EnableThinking;

                    object request;
                    if (!string.IsNullOrWhiteSpace(systemPrompt))
                    {
                        request = new
                        {
                            model = modelName,
                            messages = currentMessages,
                            stream = false,
                            tools = tools,
                            think = thinkValue,
                            system = systemPrompt,
                            options = requestOptions
                        };
                    }
                    else
                    {
                        request = new
                        {
                            model = modelName,
                            messages = currentMessages,
                            stream = false,
                            tools = tools,
                            think = thinkValue,
                            options = requestOptions
                        };
                    }

                    // Verbose logging for the request (only on first iteration to avoid spam)
                    if (_args.Verbose && iteration == 0)
                    {
                        var totalChars = messages.Sum(m => m.Content.Length);
                        Log($"    [cyan]--- Tools Chat Request ---[/]");
                        Log($"    [dim]Model: {modelName}, Messages: {messages.Count}, Chars: ~{totalChars}, Context: {contextLength}[/]");
                        Log($"    [dim]Tools: {string.Join(", ", tools.Select(t => ((dynamic)t).function.name))}[/]");

                        // Show system prompt if present
                        if (!string.IsNullOrWhiteSpace(systemPrompt))
                        {
                            Log($"    [magenta][[SYSTEM]][/]");
                            foreach (var line in systemPrompt.Split('\n'))
                            {
                                Log($"      [dim]{Markup.Escape(line)}[/]");
                            }
                        }

                        // Show full messages (includes context prepended to first question)
                        foreach (var m in messages)
                        {
                            Log($"    [yellow][[{m.Role.ToUpper()}]][/]");
                            foreach (var line in m.Content.Split('\n'))
                            {
                                Log($"      [dim]{Markup.Escape(line)}[/]");
                            }
                        }
                        Log($"    [cyan]--- End Tools Chat Request ---[/]");
                    }

                    // Use per-request timeout via CancellationTokenSource
                    // Link with main cancellation token for immediate cancellation during testing
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_args.Timeout));
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, _cts.Token);
                    var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", request, linkedCts.Token);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(linkedCts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (_args.Verbose)
                    {
                        // Console: show truncated preview
                        var preview = json.Length > 500 ? json.Substring(0, 500) + "..." : json;
                        AnsiConsole.MarkupLine($"        [dim]Tool API Response: {Markup.Escape(preview)}[/]");

                        // Log file: write full pretty-printed JSON
                        if (_logger != null)
                        {
                            var prettyJson = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                            _logger.WriteLine("        Tool API Response:");
                            foreach (var line in prettyJson.Split('\n'))
                            {
                                _logger.WriteLine($"          {line}");
                            }
                        }
                    }

                    // Check if model wants to call tools
                    if (root.TryGetProperty("message", out var msg) && msg.TryGetProperty("tool_calls", out var toolCalls))
                    {
                        // Process tool calls
                        var assistantMessage = new Dictionary<string, object>
                        {
                            ["role"] = "assistant",
                            ["content"] = msg.TryGetProperty("content", out var content) ? content.GetString() ?? "" : ""
                        };

                        var toolCallsList = new List<object>();
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            var function = tc.GetProperty("function");
                            var toolName = function.GetProperty("name").GetString() ?? "";
                            var arguments = function.GetProperty("arguments");
                            var argumentsNode = System.Text.Json.Nodes.JsonNode.Parse(arguments.GetRawText());

                            // Parse arguments to a proper object for correct serialization
                            var argsObject = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments.GetRawText());

                            toolCallsList.Add(new
                            {
                                function = new { name = toolName, arguments = argsObject }
                            });

                            // Execute tool and track usage
                            var toolResult = BenchTools.ExecuteTool(toolName, argumentsNode);
                            var argsJson = arguments.GetRawText();

                            // Track tool usage in result
                            var existingUsage = result.ToolsUsed.FirstOrDefault(t => t.ToolName == toolName);
                            if (existingUsage != null)
                            {
                                existingUsage.CallCount++;
                                existingUsage.ArgumentsJson = argsJson;
                                existingUsage.Result = toolResult;
                            }
                            else
                            {
                                result.ToolsUsed.Add(new BenchToolUsage
                                {
                                    ToolName = toolName,
                                    CallCount = 1,
                                    ArgumentsJson = argsJson,
                                    Result = toolResult
                                });
                            }

                            if (_args.Verbose)
                            {
                                Log($"        [magenta]Tool Call:[/] {toolName}");
                                Log($"          [dim]Arguments: {Markup.Escape(argsJson)}[/]");
                                Log($"          [dim]Result: {Markup.Escape(toolResult)}[/]");
                            }
                        }

                        assistantMessage["tool_calls"] = toolCallsList;
                        currentMessages.Add(assistantMessage);

                        // Add tool results
                        foreach (var tc in toolCalls.EnumerateArray())
                        {
                            var function = tc.GetProperty("function");
                            var toolName = function.GetProperty("name").GetString() ?? "";
                            var arguments = function.GetProperty("arguments");
                            var argumentsNode = System.Text.Json.Nodes.JsonNode.Parse(arguments.GetRawText());
                            var toolResult = BenchTools.ExecuteTool(toolName, argumentsNode);

                            currentMessages.Add(new
                            {
                                role = "tool",
                                content = toolResult
                            });
                        }

                        // Continue to next iteration to get final response
                        continue;
                    }

                    // No tool calls - return final response with performance metrics
                    var finalResponse = new BenchChatResponse
                    {
                        Content = msg.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "",
                        PromptTokens = root.TryGetProperty("prompt_eval_count", out var pt) ? pt.GetInt32() : null,
                        CompletionTokens = root.TryGetProperty("eval_count", out var ec) ? ec.GetInt32() : null,
                        TotalDurationNs = root.TryGetProperty("total_duration", out var td) ? td.GetInt64() : null,
                        LoadDurationNs = root.TryGetProperty("load_duration", out var ld) ? ld.GetInt64() : null,
                        PromptEvalDurationNs = root.TryGetProperty("prompt_eval_duration", out var ped) ? ped.GetInt64() : null,
                        EvalDurationNs = root.TryGetProperty("eval_duration", out var ed) ? ed.GetInt64() : null
                    };

                    if (_args.Verbose)
                    {
                        Log($"    [dim]Performance: prompt={finalResponse.PromptToksPerSec:F1} tok/s, eval={finalResponse.EvalToksPerSec:F1} tok/s, total={finalResponse.TotalDurationMs}ms[/]");
                    }

                    return finalResponse;
                }
                catch (Exception ex)
                {
                    if (_args.Verbose)
                        Log($"[red]Tool chat error: {ex.Message}[/]");
                    return null;
                }
            }

            return null; // Max iterations reached
        }

        private Dictionary<string, object> BuildOptions(int contextLength)
        {
            var options = new Dictionary<string, object>
            {
                ["num_ctx"] = contextLength
            };

            // Only set num_predict if explicitly specified in test suite
            if (_testSuite.NumPredict.HasValue)
                options["num_predict"] = _testSuite.NumPredict.Value;

            // Note: think parameter is now handled at the request level, not in options

            if (_args.Temperature.HasValue)
                options["temperature"] = _args.Temperature.Value;
            options["seed"] = _args.Seed;  // Always set seed (default: 365)
            if (_args.TopP.HasValue)
                options["top_p"] = _args.TopP.Value;
            if (_args.TopK.HasValue)
                options["top_k"] = _args.TopK.Value;
            if (_args.RepeatPenalty.HasValue)
                options["repeat_penalty"] = _args.RepeatPenalty.Value;
            if (_args.FrequencyPenalty.HasValue)
                options["frequency_penalty"] = _args.FrequencyPenalty.Value;

            return options;
        }

        #endregion

        #region Judgment

        /// <summary>
        /// Strip thinking content from model answer before sending to judge.
        /// Removes content between &lt;think&gt; and &lt;/think&gt; tags.
        /// Also handles case where only &lt;/think&gt; is present (strips everything before it).
        /// </summary>
        private static string StripThinkingFromAnswer(string answer)
        {
            if (string.IsNullOrEmpty(answer))
                return answer;

            var result = answer;

            // Remove <think>...</think> blocks (case-insensitive, handles multiline)
            result = System.Text.RegularExpressions.Regex.Replace(
                result,
                @"<think>.*?</think>",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

            // Handle case where only </think> is present (no opening tag)
            // Strip everything from start up to and including </think>
            var closeTagIndex = result.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (closeTagIndex >= 0)
            {
                result = result.Substring(closeTagIndex + "</think>".Length);
            }

            return result.Trim();
        }

        private async Task<JudgeResult?> JudgeAnswerAsync(
            BenchQuestion question,
            string modelAnswer,
            BenchCategory category,
            BenchSubCategory? subCategory)
        {
            // Strip thinking content from model answer before sending to judge
            var answerForJudge = StripThinkingFromAnswer(modelAnswer);

            // Get the appropriate judge prompt (with hierarchical overrides)
            var judgePrompt = question.JudgePrompt
                ?? subCategory?.JudgePrompt
                ?? category.JudgePrompt
                ?? _testSuite.JudgePrompt
                ?? GetDefaultJudgePrompt();

            var judgeSystemPrompt = question.JudgeSystemPrompt
                ?? subCategory?.JudgeSystemPrompt
                ?? category.JudgeSystemPrompt
                ?? _testSuite.JudgeSystemPrompt
                ?? JUDGE_SYSTEM_PROMPT;

            // Build the prompt with substitutions
            var prompt = judgePrompt
                .Replace("%%QUESTION%%", question.Text)
                .Replace("%%REF_ANSWER%%", question.ReferenceAnswer)
                .Replace("%%MODEL_ANSWER%%", answerForJudge);

            if (_args.Verbose)
            {
                Log($"        [blue]--- Judge Request for Q{question.Id} ---[/]");
                Log($"        [yellow][[SYSTEM]][/]");
                foreach (var line in judgeSystemPrompt.Split('\n'))
                {
                    Log($"          [dim]{Markup.Escape(line)}[/]");
                }
                Log($"        [yellow][[USER]][/]");
                foreach (var line in prompt.Split('\n'))
                {
                    Log($"          [dim]{Markup.Escape(line)}[/]");
                }
                Log($"        [blue]--- End Judge Request ---[/]");
            }

            try
            {
                string jsonResponse;
                var result = new JudgeResult();

                if (_cloudJudgeProvider != null)
                {
                    // Use cloud provider (no metrics available)
                    var response = await _cloudJudgeProvider.JudgeAsync(judgeSystemPrompt, prompt, 1024);
                    jsonResponse = response.RawResponse ?? "";
                }
                else if (_judgeHttpClient != null && _judgeBaseUrl != null && _judgeModelName != null)
                {
                    // Use Ollama
                    var judgeContextLength = _args.JudgeCtxSize > 0
                        ? _args.JudgeCtxSize
                        : Math.Max(8192, (prompt.Length / 4) * 2 + 2048);

                    var request = new
                    {
                        model = _judgeModelName,
                        messages = new[]
                        {
                            new { role = "system", content = judgeSystemPrompt },
                            new { role = "user", content = prompt }
                        },
                        stream = false,
                        format = "json",
                        options = new { num_ctx = judgeContextLength, num_predict = 8192 }
                    };

                    // Use per-request timeout via CancellationTokenSource
                    using var judgeTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_args.Timeout));
                    var response = await _judgeHttpClient.PostAsJsonAsync($"{_judgeBaseUrl}/api/chat", request, judgeTimeoutCts.Token);
                    response.EnsureSuccessStatusCode();

                    var json = await response.Content.ReadAsStringAsync(judgeTimeoutCts.Token);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    jsonResponse = root.GetProperty("message").GetProperty("content").GetString() ?? "";

                    // Extract performance metrics from Ollama response
                    if (root.TryGetProperty("prompt_eval_count", out var promptEvalCount))
                        result.PromptTokens = promptEvalCount.GetInt32();
                    if (root.TryGetProperty("eval_count", out var evalCount))
                        result.EvalTokens = evalCount.GetInt32();
                    if (root.TryGetProperty("prompt_eval_duration", out var promptEvalDuration))
                        result.PromptDurationMs = promptEvalDuration.GetInt64() / 1_000_000.0; // ns to ms
                    if (root.TryGetProperty("eval_duration", out var evalDuration))
                        result.EvalDurationMs = evalDuration.GetInt64() / 1_000_000.0; // ns to ms
                }
                else
                {
                    return null;
                }

                if (_args.Verbose)
                {
                    Log($"        [blue]--- Judge Response ---[/]");
                    foreach (var line in jsonResponse.Split('\n'))
                    {
                        Log($"          [dim]{Markup.Escape(line)}[/]");
                    }
                    Log($"        [blue]--- End Judge Response ---[/]");
                }

                // Parse judgment response
                result.Judgment = ParseJudgmentResponse(jsonResponse);
                return result;
            }
            catch (Exception ex)
            {
                if (_args.Verbose)
                    Log($"[yellow]Judgment error: {ex.Message}[/]");
                return null;
            }
        }

        private BenchJudgment? ParseJudgmentResponse(string jsonResponse)
        {
            try
            {
                // Try to extract JSON from response
                var jsonStart = jsonResponse.IndexOf('{');
                var jsonEnd = jsonResponse.LastIndexOf('}');

                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    var jsonPart = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
                    using var doc = JsonDocument.Parse(jsonPart);
                    var root = doc.RootElement;

                    var answer = root.TryGetProperty("Answer", out var a)
                        ? a.GetString()
                        : root.TryGetProperty("answer", out var a2) ? a2.GetString() : null;

                    var reason = root.TryGetProperty("Reason", out var r)
                        ? r.GetString()
                        : root.TryGetProperty("reason", out var r2) ? r2.GetString() : null;

                    return new BenchJudgment
                    {
                        Answer = answer ?? "NO",
                        Reason = reason ?? "",
                        JudgedAt = DateTime.UtcNow,
                        RawResponse = jsonResponse
                    };
                }
            }
            catch { }

            // Fallback: look for YES/NO in response
            var upperResponse = jsonResponse.ToUpperInvariant();
            return new BenchJudgment
            {
                Answer = upperResponse.Contains("YES") ? "YES" : "NO",
                Reason = "Parsed from response text",
                JudgedAt = DateTime.UtcNow,
                RawResponse = jsonResponse
            };
        }

        private string GetDefaultJudgePrompt()
        {
            return @"Evaluate if the model's answer is correct compared to the reference answer.

Question: %%QUESTION%%

Reference Answer: %%REF_ANSWER%%

Model Answer: %%MODEL_ANSWER%%

Respond in JSON format with two fields:
- ""Answer"": ""YES"" if the model answer is correct or equivalent, ""NO"" if incorrect
- ""Reason"": Brief explanation of your judgment

Consider an answer correct if it conveys the same meaning, even if worded differently.";
        }

        private async Task<bool> VerifyJudgeModelExistsAsync()
        {
            try
            {
                var response = await _judgeHttpClient!.GetFromJsonAsync<OllamaModelsResponse>($"{_judgeBaseUrl}/api/tags");
                return response?.models?.Any(m => m.name.Equals(_judgeModelName, StringComparison.OrdinalIgnoreCase)) ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Get all question results from a category, including those in subcategories
        /// </summary>
        private List<BenchQuestionResult> GetAllQuestionResults(BenchCategoryResult categoryResult)
        {
            var results = new List<BenchQuestionResult>();

            if (categoryResult.QuestionResults != null)
            {
                results.AddRange(categoryResult.QuestionResults);
            }

            if (categoryResult.SubCategoryResults != null)
            {
                foreach (var subCategory in categoryResult.SubCategoryResults)
                {
                    results.AddRange(subCategory.QuestionResults);
                }
            }

            return results;
        }

        #endregion

        #region Help and Special Commands

        private int GenerateTestSuites()
        {
            try
            {
                var directory = Directory.GetCurrentDirectory();

                // Determine which test types to generate
                var testType = _args.TestSuite?.ToLowerInvariant();
                var generateCtxBench = string.IsNullOrEmpty(testType) || testType == "ctxbench";
                var generateCtxToolsBench = string.IsNullOrEmpty(testType) || testType == "ctxtoolsbench";

                if (!generateCtxBench && !generateCtxToolsBench)
                {
                    Log($"[red]Error: Unknown test type '{_args.TestSuite}'. Use 'ctxbench' or 'ctxtoolsbench'.[/]");
                    return 1;
                }

                Log($"[cyan]Generating test suite(s)...[/]");

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                };

                if (generateCtxBench)
                {
                    // Determine output path
                    string ctxBenchPath;
                    if (!string.IsNullOrEmpty(_args.OutputFile) && !generateCtxToolsBench)
                    {
                        // Use specified output file when generating single suite
                        ctxBenchPath = Path.IsPathRooted(_args.OutputFile)
                            ? _args.OutputFile
                            : Path.Combine(directory, _args.OutputFile);
                    }
                    else
                    {
                        ctxBenchPath = Path.Combine(directory, "v1ctxbench.json");
                    }

                    // Check for overwrite
                    if (File.Exists(ctxBenchPath) && !_args.Overwrite)
                    {
                        if (!AnsiConsole.Confirm($"[yellow]File '{ctxBenchPath}' already exists. Overwrite?[/]", false))
                        {
                            Log("[dim]Skipping ctxbench generation.[/]");
                            if (!generateCtxToolsBench) return 0;
                        }
                        else
                        {
                            GenerateAndSaveSuite(false, ctxBenchPath, options);
                        }
                    }
                    else
                    {
                        GenerateAndSaveSuite(false, ctxBenchPath, options);
                    }
                }

                if (generateCtxToolsBench)
                {
                    // Determine output path
                    string ctxToolsBenchPath;
                    if (!string.IsNullOrEmpty(_args.OutputFile) && !generateCtxBench)
                    {
                        // Use specified output file when generating single suite
                        ctxToolsBenchPath = Path.IsPathRooted(_args.OutputFile)
                            ? _args.OutputFile
                            : Path.Combine(directory, _args.OutputFile);
                    }
                    else
                    {
                        ctxToolsBenchPath = Path.Combine(directory, "v1ctxtoolsbench.json");
                    }

                    // Check for overwrite
                    if (File.Exists(ctxToolsBenchPath) && !_args.Overwrite)
                    {
                        if (!AnsiConsole.Confirm($"[yellow]File '{ctxToolsBenchPath}' already exists. Overwrite?[/]", false))
                        {
                            Log("[dim]Skipping ctxtoolsbench generation.[/]");
                            return 0;
                        }
                    }
                    GenerateAndSaveSuite(true, ctxToolsBenchPath, options);
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log($"[red]Error generating test suites: {ex.Message}[/]");
                return 1;
            }
        }

        private void GenerateAndSaveSuite(bool withTools, string path, JsonSerializerOptions options)
        {
            // Use context percent override if provided
            var generator = new BenchStoryGenerator(42, _args.ContextPercent);
            var suite = withTools ? generator.GenerateCtxToolsBenchSuite() : generator.GenerateCtxBenchSuite();
            var typeName = withTools ? "ctxtoolsbench" : "ctxbench";

            if (_args.ContextPercent.HasValue)
            {
                Log($"[dim]Content scaling: {_args.ContextPercent}% (100% = calibrated default)[/]");
            }

            Log($"\n[dim]Validating {typeName} context usage...[/]");
            BenchStoryGenerator.PrintContextValidation(suite);

            File.WriteAllText(path, JsonSerializer.Serialize(suite, options));
            Log($"[green]Created: {path}[/]");
        }

        private void PrintToolsCheatSheet()
        {
            Log("[bold]Available Benchmark Tools[/]\n");
            BenchTools.PrintToolsCheatSheet();
        }

        private void PrintCloudProviderHelp()
        {
            Log(@"[bold]Cloud Provider Support for --judge[/]

[bold]Format:[/] @provider[:token]/model

[bold]Providers:[/]
  @claude      Anthropic Claude (env: ANTHROPIC_API_KEY)
               Models: claude-sonnet-4-20250514, claude-opus-4-20250514, etc.

  @openai      OpenAI (env: OPENAI_API_KEY)
               Models: gpt-4o, gpt-4o-mini, o1, o1-mini, etc.

  @gemini      Google Gemini (env: GEMINI_API_KEY or GOOGLE_API_KEY)
               Models: gemini-2.0-flash, gemini-1.5-pro, etc.

  @huggingface HuggingFace Inference (env: HF_TOKEN)
               Models: meta-llama/Llama-3.3-70B-Instruct, etc.

  @azure       Azure OpenAI (env: AZURE_OPENAI_API_KEY + AZURE_OPENAI_ENDPOINT)
               Format: @azure[:key@endpoint]/deployment-name

  @cohere      Cohere (env: CO_API_KEY or COHERE_API_KEY)
               Models: command-a-03-2025, command-r-plus, etc.

  @mistral     Mistral AI (env: MISTRAL_API_KEY)
               Models: mistral-large-latest, mistral-medium, etc.

  @together    Together AI (env: TOGETHER_API_KEY)
               Models: meta-llama/Llama-3.3-70B-Instruct, etc.

  @replicate   Replicate (env: REPLICATE_API_TOKEN)
               Models: meta/llama-2-70b-chat, etc.

[bold]Examples:[/]
  osync bench -M llama3.2 -Q q4*,q8* -T v1ctxbench.json --judge @claude/claude-sonnet-4-20250514
  osync bench -M qwen3 -Q 4b -T v1ctxtoolsbench.json --judge @openai/gpt-4o
  osync bench -M phi4 -Q * -T custom.json --judge @gemini/gemini-2.0-flash
");
        }

        #endregion

        #region Calibration

        /// <summary>
        /// Initialize calibration tracking for a category
        /// Note: We do NOT reset cumulative counters here because conversation
        /// content carries over between categories. The cumulative totals should
        /// continue to grow to match the actual prompt tokens.
        /// </summary>
        private void InitializeCategoryCalibration(BenchCategory category)
        {
            if (_calibrationData == null) return;

            // DON'T reset counters - conversation carries over between categories
            // _cumulativeEstimatedTokens = 0;
            // _cumulativeCharCount = 0;

            var targetFillPercent = category.ContextLength <= 4096 ? 0.90 : 0.95;

            _currentCalibrationCategory = new CalibrationCategory
            {
                Name = category.Name,
                TargetContextLength = category.ContextLength,
                TargetFillPercent = targetFillPercent
            };
        }

        /// <summary>
        /// Track a calibration step
        /// </summary>
        private void TrackCalibrationStep(string stepName, string stepType, string text, int actualPromptTokens, int actualOutputTokens)
        {
            if (_currentCalibrationCategory == null) return;

            var charCount = text?.Length ?? 0;
            _cumulativeCharCount += charCount;

            // Use context-dependent ratio for the CURRENT category
            // Recalculate cumulative estimate based on total chars, not incremental
            // This is more accurate because the model tokenizes all content together
            var contextLength = _currentCalibrationCategory.TargetContextLength;
            var charsPerToken = BenchTokenizer.GetCharsPerTokenForContext(contextLength);
            var estimatedTokens = (int)Math.Ceiling(charCount / charsPerToken);
            _cumulativeEstimatedTokens = (int)Math.Ceiling(_cumulativeCharCount / charsPerToken);

            var delta = actualPromptTokens - _cumulativeEstimatedTokens;
            var deltaPercent = _cumulativeEstimatedTokens > 0
                ? (delta * 100.0 / _cumulativeEstimatedTokens)
                : 0;

            // Calculate effective chars/token from actual
            var effectiveCharsPerToken = actualPromptTokens > 0
                ? (double)_cumulativeCharCount / actualPromptTokens
                : BenchTokenizer.DefaultCharsPerToken;

            var step = new CalibrationStep
            {
                StepName = stepName,
                StepType = stepType,
                CharCount = charCount,
                EstimatedTokens = estimatedTokens,
                CumulativeEstimate = _cumulativeEstimatedTokens,
                ActualPromptTokens = actualPromptTokens,
                ActualOutputTokens = actualOutputTokens,
                Delta = delta,
                DeltaPercent = deltaPercent,
                EffectiveCharsPerToken = effectiveCharsPerToken,
                TextPreview = text?.Length > 100 ? text.Substring(0, 100) + "..." : text ?? ""
            };

            _currentCalibrationCategory.Steps.Add(step);

            // Log calibration step if verbose
            if (_args.Verbose)
            {
                var deltaColor = Math.Abs(deltaPercent) <= 5 ? "green" : (Math.Abs(deltaPercent) <= 10 ? "yellow" : "red");
                Log($"    [dim]CALIB {stepName}: {charCount} chars, est={estimatedTokens} (cumul={_cumulativeEstimatedTokens}), actual={actualPromptTokens}[/], delta=[{deltaColor}]{delta:+#;-#;0} ({deltaPercent:+0.0;-0.0;0.0}%)[/]");
            }
        }

        /// <summary>
        /// Finalize calibration for current category and print summary
        /// </summary>
        private void FinalizeCategoryCalibration()
        {
            if (_currentCalibrationCategory == null || _calibrationData == null) return;

            var steps = _currentCalibrationCategory.Steps;
            if (steps.Count == 0) return;

            var lastStep = steps.Last();

            // Calculate summary statistics
            var summary = new CalibrationSummary
            {
                TotalChars = _cumulativeCharCount,
                TotalEstimatedTokens = _cumulativeEstimatedTokens,
                FinalActualPromptTokens = lastStep.ActualPromptTokens,
                FinalActualOutputTokens = lastStep.ActualOutputTokens,
                OverallDelta = lastStep.Delta,
                OverallDeltaPercent = lastStep.DeltaPercent,
                AvgEffectiveCharsPerToken = lastStep.EffectiveCharsPerToken,
                RecommendedCharsPerToken = _cumulativeCharCount > 0 && lastStep.ActualPromptTokens > 0
                    ? (double)_cumulativeCharCount / lastStep.ActualPromptTokens
                    : BenchTokenizer.DefaultCharsPerToken,
                ContextFillPercent = _currentCalibrationCategory.TargetContextLength > 0
                    ? (lastStep.ActualPromptTokens * 100.0 / _currentCalibrationCategory.TargetContextLength)
                    : 0,
                WithinTolerance = Math.Abs(lastStep.DeltaPercent) <= 5.0
            };

            _currentCalibrationCategory.Summary = summary;

            // Store in calibration data
            _calibrationData.Categories[_currentCalibrationCategory.Name] = _currentCalibrationCategory;

            // Print calibration table
            PrintCalibrationTable();
        }

        /// <summary>
        /// Print calibration table for current category
        /// </summary>
        private void PrintCalibrationTable()
        {
            if (_currentCalibrationCategory == null) return;

            var cat = _currentCalibrationCategory;
            var summary = cat.Summary;

            Log("");
            Log($"[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]");
            Log($"[bold cyan] TOKEN CALIBRATION: {_calibrationData?.ModelName ?? "?"} - Category {cat.Name} ({cat.TargetContextLength} tokens)[/]");
            Log($"[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]");

            // Create table
            var table = new Table()
                .Border(TableBorder.Rounded)
                .AddColumn("Step")
                .AddColumn(new TableColumn("Chars").RightAligned())
                .AddColumn(new TableColumn("Est.Tok").RightAligned())
                .AddColumn(new TableColumn("Cumul.Est").RightAligned())
                .AddColumn(new TableColumn("Actual").RightAligned())
                .AddColumn(new TableColumn("Delta").RightAligned())
                .AddColumn(new TableColumn("Delta%").RightAligned())
                .AddColumn(new TableColumn("Eff.C/T").RightAligned());

            foreach (var step in cat.Steps)
            {
                var deltaColor = Math.Abs(step.DeltaPercent) <= 5 ? "green" : (Math.Abs(step.DeltaPercent) <= 10 ? "yellow" : "red");
                var deltaSign = step.Delta >= 0 ? "+" : "";

                table.AddRow(
                    step.StepName,
                    step.CharCount.ToString("N0"),
                    step.EstimatedTokens.ToString("N0"),
                    step.CumulativeEstimate.ToString("N0"),
                    step.ActualPromptTokens.ToString("N0"),
                    $"[{deltaColor}]{deltaSign}{step.Delta}[/]",
                    $"[{deltaColor}]{step.DeltaPercent:+0.0;-0.0;0.0}%[/]",
                    step.EffectiveCharsPerToken.ToString("F2")
                );
            }

            AnsiConsole.Write(table);

            // Print summary
            var toleranceColor = summary.WithinTolerance ? "green" : "red";
            var toleranceText = summary.WithinTolerance ? "PASS" : "FAIL";

            Log("");
            Log($"[bold]Summary:[/]");
            Log($"  Total chars: {summary.TotalChars:N0}");
            Log($"  Estimated tokens: {summary.TotalEstimatedTokens:N0}");
            Log($"  Actual prompt tokens: {summary.FinalActualPromptTokens:N0}");
            Log($"  Overall delta: [{toleranceColor}]{summary.OverallDelta:+#;-#;0} ({summary.OverallDeltaPercent:+0.0;-0.0;0.0}%)[/]");
            Log($"  Context fill: {summary.ContextFillPercent:F1}% of {cat.TargetContextLength} (target: {cat.TargetFillPercent * 100:F0}%)");
            Log($"  Avg effective chars/token: {summary.AvgEffectiveCharsPerToken:F2}");
            Log($"  Recommended chars/token: {summary.RecommendedCharsPerToken:F2}");
            Log($"  Tolerance check (within 5%): [{toleranceColor}]{toleranceText}[/]");
            Log($"[bold cyan]═══════════════════════════════════════════════════════════════════════════════[/]");
            Log("");
        }

        /// <summary>
        /// Save calibration data to file
        /// </summary>
        private async Task SaveCalibrationDataAsync()
        {
            if (_calibrationData == null) return;

            // Determine output path
            string outputPath;
            if (!string.IsNullOrWhiteSpace(_args.CalibrateOutput))
            {
                outputPath = _args.CalibrateOutput;
            }
            else
            {
                // Default: calibration_<model>_<quant>.json in same directory as test suite
                var directory = Path.GetDirectoryName(_testSuiteFilePath) ?? ".";
                var modelName = _args.ModelName.Replace(":", "_").Replace("/", "_");
                var quant = _args.Quants.Replace(",", "_").Replace("*", "").Replace(":", "_");
                outputPath = Path.Combine(directory, $"calibration_{modelName}_{quant}.json");
            }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_calibrationData, options);
                await File.WriteAllTextAsync(outputPath, json);
                Log($"[green]Calibration data saved to: {outputPath}[/]");
            }
            catch (Exception ex)
            {
                Log($"[yellow]Warning: Could not save calibration data: {ex.Message}[/]");
            }
        }

        #endregion

        #region Fix Results File

        /// <summary>
        /// Attempt to fix a corrupted/malformed bench results file
        /// </summary>
        private async Task<int> FixResultsFileAsync()
        {
            // Determine input file path
            string inputPath;
            if (!string.IsNullOrEmpty(_args.OutputFile))
            {
                inputPath = _args.OutputFile;
            }
            else if (!string.IsNullOrWhiteSpace(_args.ModelName))
            {
                // Try common file names
                var sanitizedName = _args.ModelName.Replace('/', '-').Replace('\\', '-');
                inputPath = $"{sanitizedName}.bench.json";
                if (!File.Exists(inputPath))
                    inputPath = $"{sanitizedName}.ctxbench.json";
                if (!File.Exists(inputPath))
                    inputPath = $"{sanitizedName}.ctxtoolsbench.json";
            }
            else
            {
                Log("[red]Error: Specify the file to fix with -O <filename> or -M <modelname>[/]");
                return 1;
            }

            if (!File.Exists(inputPath))
            {
                Log($"[red]Error: File not found: {inputPath}[/]");
                return 1;
            }

            // Determine output file path (never overwrite original)
            var outputPath = Path.ChangeExtension(inputPath, ".fixed.json");
            if (inputPath.EndsWith(".fixed.json", StringComparison.OrdinalIgnoreCase))
            {
                outputPath = inputPath.Replace(".fixed.json", $".fixed_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            }

            Log($"[cyan]Attempting to fix: {inputPath}[/]");
            Log($"[dim]Output will be saved to: {outputPath}[/]");

            // Read the raw content
            string rawContent;
            try
            {
                rawContent = await File.ReadAllTextAsync(inputPath);
                Log($"[dim]Read {rawContent.Length:N0} characters from file[/]");
            }
            catch (Exception ex)
            {
                Log($"[red]Error reading file: {Markup.Escape(ex.Message)}[/]");
                return 1;
            }

            // Try normal parsing first
            BenchResultsFile? resultsFile = null;
            try
            {
                resultsFile = JsonSerializer.Deserialize<BenchResultsFile>(rawContent);
                if (resultsFile != null)
                {
                    Log("[green]File parsed successfully - no JSON errors![/]");
                    Log($"[dim]Found {resultsFile.Results?.Count ?? 0} tag results[/]");

                    // Clean up and validate data
                    var fixCount = CleanupBenchResults(resultsFile);
                    if (fixCount > 0)
                    {
                        Log($"[cyan]Applied {fixCount} data cleanup(s)[/]");

                        // Save the cleaned file
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        var cleanedJson = JsonSerializer.Serialize(resultsFile, options);
                        await File.WriteAllTextAsync(outputPath, cleanedJson);
                        Log($"[green]Cleaned file saved to: {outputPath}[/]");

                        // Show file size change
                        var originalSize = new FileInfo(inputPath).Length;
                        var newSize = new FileInfo(outputPath).Length;
                        var sizeDiff = newSize - originalSize;
                        var diffStr = sizeDiff >= 0 ? $"+{sizeDiff:N0}" : sizeDiff.ToString("N0");
                        Log($"[dim]  Original: {originalSize:N0} bytes, Fixed: {newSize:N0} bytes ({diffStr} bytes)[/]");
                    }
                    else
                    {
                        Log("[dim]No data cleanup needed - file is already clean[/]");
                    }
                    return 0;
                }
            }
            catch (JsonException ex)
            {
                Log($"[yellow]JSON parse error at position {ex.BytePositionInLine ?? 0}: {Markup.Escape(ex.Message)}[/]");
                Log("[dim]Attempting recovery...[/]");
            }

            // Try to recover truncated JSON
            Log("[dim]Attempting structural recovery...[/]");
            var (fixedContent, stats) = TryRecoverBenchJson(rawContent);
            if (fixedContent != null)
            {
                try
                {
                    resultsFile = JsonSerializer.Deserialize<BenchResultsFile>(fixedContent);
                    if (resultsFile != null)
                    {
                        Log($"[green]Recovery successful![/]");
                        if (stats.TruncatedArrays > 0)
                            Log($"[dim]  Fixed {stats.TruncatedArrays} truncated array(s)[/]");
                        if (stats.TruncatedObjects > 0)
                            Log($"[dim]  Fixed {stats.TruncatedObjects} truncated object(s)[/]");
                        if (stats.RemovedBytes > 0)
                            Log($"[dim]  Removed {stats.RemovedBytes:N0} bytes of corrupted data[/]");

                        // Clean up recovered data
                        CleanupBenchResults(resultsFile);

                        // Save the fixed file
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        var fixedJson = JsonSerializer.Serialize(resultsFile, options);
                        await File.WriteAllTextAsync(outputPath, fixedJson);
                        Log($"[green]Fixed file saved to: {outputPath}[/]");

                        // Show summary
                        Log($"\n[cyan]Summary of recovered data:[/]");
                        foreach (var result in resultsFile.Results ?? new List<BenchQuantResult>())
                        {
                            var categoryCount = result.CategoryResults?.Count ?? 0;
                            var isComplete = result.IsComplete ? "complete" : "incomplete";
                            Log($"  [dim]• {result.Tag}: {categoryCount} categories ({isComplete})[/]");
                        }

                        return 0;
                    }
                }
                catch (JsonException ex2)
                {
                    Log($"[dim]Recovery produced invalid JSON: {Markup.Escape(ex2.Message)}[/]");
                }
            }

            Log("[red]Error: Could not recover valid JSON structure[/]");

            // Save partial content for manual inspection
            if (fixedContent != null)
            {
                var partialPath = Path.ChangeExtension(inputPath, ".partial.json");
                await File.WriteAllTextAsync(partialPath, fixedContent);
                Log($"[yellow]Saved partial recovery to: {partialPath}[/]");
            }

            return 1;
        }

        /// <summary>
        /// Clean up bench results data
        /// Returns count of cleanups applied
        /// </summary>
        private int CleanupBenchResults(BenchResultsFile resultsFile)
        {
            int fixCount = 0;
            if (resultsFile?.Results == null) return 0;

            // Remove null or empty tag results
            var originalCount = resultsFile.Results.Count;
            resultsFile.Results = resultsFile.Results
                .Where(r => r != null && !string.IsNullOrEmpty(r.Tag))
                .ToList();
            var removed = originalCount - resultsFile.Results.Count;
            if (removed > 0)
            {
                Log($"[dim]  Removed {removed} invalid/empty tag result(s)[/]");
                fixCount += removed;
            }

            // Check for and fix common issues in each result
            foreach (var result in resultsFile.Results)
            {
                // Update osyncVersion if it's the old format
                if (result.OsyncVersion == "1.0.0.0" || result.OsyncVersion == null)
                {
                    result.OsyncVersion = GetOsyncVersion();
                    fixCount++;
                }

                // Recalculate scores if they seem wrong
                if (result.CategoryResults != null && result.CategoryResults.Any())
                {
                    var calculatedScore = BenchScoring.CalculateOverallScore(result);
                    if (Math.Abs(result.OverallScore - calculatedScore) > 0.01)
                    {
                        result.OverallScore = calculatedScore;
                        fixCount++;
                    }
                }
            }

            return fixCount;
        }

        /// <summary>
        /// Stats from JSON recovery attempt
        /// </summary>
        private record BenchRecoveryStats(int TruncatedArrays, int TruncatedObjects, int RemovedBytes);

        /// <summary>
        /// Try to recover truncated bench JSON
        /// </summary>
        private (string? FixedContent, BenchRecoveryStats Stats) TryRecoverBenchJson(string content)
        {
            int truncatedArrays = 0;
            int truncatedObjects = 0;
            int originalLength = content.Length;

            // Count brackets/braces to find what's missing
            var stack = new Stack<char>();
            bool inString = false;
            bool escapeNext = false;
            int lastValidPos = 0;

            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];

                if (escapeNext)
                {
                    escapeNext = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escapeNext = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString) continue;

                if (c == '{')
                {
                    stack.Push('}');
                    lastValidPos = i;
                }
                else if (c == '[')
                {
                    stack.Push(']');
                    lastValidPos = i;
                }
                else if (c == '}' || c == ']')
                {
                    if (stack.Count > 0 && stack.Peek() == c)
                    {
                        stack.Pop();
                        lastValidPos = i;
                    }
                }
            }

            // If balanced, nothing to fix
            if (stack.Count == 0)
            {
                return (content, new BenchRecoveryStats(0, 0, 0));
            }

            // Build the closing sequence
            var closingChars = new StringBuilder();
            while (stack.Count > 0)
            {
                var c = stack.Pop();
                if (c == ']') truncatedArrays++;
                else if (c == '}') truncatedObjects++;
                closingChars.Append(c);
            }

            // Try to find a good truncation point (after last complete element)
            var fixed_content = content.TrimEnd() + closingChars.ToString();
            var removedBytes = originalLength - content.TrimEnd().Length;

            return (fixed_content, new BenchRecoveryStats(truncatedArrays, truncatedObjects, removedBytes));
        }

        #endregion
    }
}
