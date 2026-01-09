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
        private ITestSuite _testSuite = null!;  // Initialized in ExecuteAsync before use
        private QcResultsFile _resultsFile = null!;  // Initialized in ExecuteAsync before use

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

        // Track models pulled on-demand this session for cleanup on failure
        private readonly HashSet<string> _modelsPulledThisSession = new HashSet<string>();

        // Retry configuration for API calls
        private const int MAX_RETRY_ATTEMPTS = 5;
        private const int RETRY_DELAY_MS = 1000;

        // Default context lengths
        private const int DEFAULT_TEST_CONTEXT_LENGTH = 4096;
        private const int DEFAULT_JUDGE_CONTEXT_LENGTH = 12288;

        // Track last displayed context length to show overrides
        private int _lastDisplayedContextLength = 0;

        // System prompt for the judge model - includes full instructions for redundancy
        private const string JUDGE_SYSTEM_PROMPT = @"You are a SIMILARITY JUDGE. You compare two AI-generated responses (A and B) and measure how SIMILAR they are to each other.

YOUR TASK: Score how much RESPONSE A and RESPONSE B match each other (1-100).

SCORING SCALE:
100 = Identical content and approach or RESPONSE B is significantly better
95-99 = Nearly identical content and approach
90-94 = Very similar content, nearly identical approach
85-89 = Very similar, minor differences
80-84 = Very similar, noticeable differences
75-79 = Very similar, significant differences
70-74 = Moderately similar, minor differences
65-69 = Moderately similar, noticeable differences
60-64 = Moderately similar, significant differences
51-59 = Some overlap, minor differences
41-50 = Some overlap, noticeable differences
30-40 = Some overlap, significant differences
21-29 = Completely different responses, similar content
11-20 = Completely different responses
1-10 = Completely different responses, gibberish content

CRITICAL RULES:
- You are measuring SIMILARITY between A and B, NOT quality or correctness
- If RESPONSE A and RESPONSE B both contain the same code/content = HIGH score (they match!)
- If RESPONSE A and RESPONSE B contain different code/content = LOW score (they differ)
- Do NOT base the judge scoring on the responses being correct, well-written, or valid.
- Do NOT mention JSON, language proficiency, or format compliance
- If RESPONSE B is significantly more complete and robust, better or superior then the scoring is HIGH
- IMPORTANT: Output score as INTEGER 1-100, NOT decimal 0.0-1.0. If similarity is 100%, output 100 (not 1 or 1.0). If similarity is 85%, output 85 (not 0.85).

REASON FORMAT: Start with 'A and B match:' or 'A and B differ:' then explain what each contains and why they are similar or different.";

        private const string JUDGE_USER_INSTRUCTIONS = @"Compare RESPONSE A and RESPONSE B below. Score their SIMILARITY (1-100).

Remember:
- HIGH score = A and B contain similar content
- LOW score = A and B contain different content (unless RESPONSE B is significantly better)
- Score must be INTEGER 1-100: use 100 for identical (not 1), use 85 for 85% similar (not 0.85)
- Do NOT judge quality, correctness, or format (unless RESPONSE B is significantly better)";

        public QcCommand(QcArgs args, string baseUrl = "http://localhost:11434")
        {
            _args = args;
            _baseUrl = baseUrl.TrimEnd('/');

            // Apply default timeout if not set (PowerArgs may not apply default value)
            if (_args.Timeout <= 0)
                _args.Timeout = 600;

            // Apply default judge context size if not set
            if (_args.JudgeCtxSize <= 0)
                _args.JudgeCtxSize = DEFAULT_JUDGE_CONTEXT_LENGTH;

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

            // If --rejudge is set, always re-run judgment
            if (_args.Rejudge)
                return true;

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

                // Print OnDemand mode status
                if (_args.OnDemand)
                {
                    AnsiConsole.MarkupLine($"[cyan]On-demand mode: Models will be pulled if missing and removed after testing[/]");
                }

                // Parse quantization tags to test
                var rawQuantTags = _args.Quants.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(q => q.Trim())
                    .ToList();

                if (rawQuantTags.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: No quantization tags specified[/]");
                    return 1;
                }

                // Expand wildcards in quantization tags
                var quantTags = await ExpandWildcardTagsAsync(rawQuantTags);

                if (quantTags.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: No quantization tags found after wildcard expansion[/]");
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
                // Store original full base model name if specified as a full path (e.g., hf.co/namespace/repo:tag)
                string? originalBaseModelFullName = null;

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
                    // BaseTag contains a full model name (e.g., hf.co/namespace/repo:tag or model:tag)
                    // Store the full model name so it's used as-is for testing
                    originalBaseModelFullName = _args.BaseTag;

                    // Extract just the tag portion for comparison/tracking
                    var colonIndex = _args.BaseTag.LastIndexOf(':');
                    _args.BaseTag = _args.BaseTag.Substring(colonIndex + 1);
                }

                // Ensure base tag is included if not already present
                // Include base if: no base exists yet, OR base exists but is incomplete (needs resume)
                bool baseNeedsTesting = existingBase == null ||
                    (existingBase.QuestionResults.Count < _testSuite.TotalQuestions && !_args.Force);
                bool baseNeedsForceRerun = existingBase != null && _args.Force;

                // If base was specified as full model name, insert that; otherwise insert just the tag
                string baseTagToInsert = originalBaseModelFullName ?? _args.BaseTag;
                if ((baseNeedsTesting || baseNeedsForceRerun) && !quantTags.Contains(_args.BaseTag) && !quantTags.Contains(baseTagToInsert))
                {
                    quantTags.Insert(0, baseTagToInsert);
                }

                // Pre-verify all models exist at startup
                AnsiConsole.MarkupLine($"[dim]Verifying {quantTags.Count} model(s)...[/]");
                var missingModels = new List<string>();
                var unavailableModels = new List<string>();
                var modelsVerifiedMissing = new HashSet<string>(); // Track models that were missing at startup
                _modelsPulledThisSession.Clear(); // Reset tracking for this session

                foreach (var tag in quantTags)
                {
                    string modelFullName = tag.Contains(':') ? tag : $"{_args.ModelName}:{tag}";

                    // Check if already tested (skip verification for complete results unless forced)
                    var existingResult = _resultsFile.Results.FirstOrDefault(r => r?.Tag == (tag.Contains(':') ? tag.Substring(tag.LastIndexOf(':') + 1) : tag));
                    if (existingResult != null && !_args.Force)
                    {
                        var isComplete = existingResult.QuestionResults.Count >= _testSuite.TotalQuestions;
                        if (isComplete)
                        {
                            continue; // Skip verification for already-tested models
                        }
                    }

                    if (!await CheckModelExistsAsync(modelFullName))
                    {
                        missingModels.Add(modelFullName);
                        modelsVerifiedMissing.Add(modelFullName);

                        // If on-demand mode, verify model exists in registry
                        if (_args.OnDemand)
                        {
                            AnsiConsole.MarkupLine($"[dim]  Checking registry for {modelFullName}...[/]");
                            if (!await CheckModelExistsInRegistryAsync(modelFullName))
                            {
                                unavailableModels.Add(modelFullName);
                            }
                        }
                    }
                }

                // If any models are unavailable in registry, exit
                if (unavailableModels.Count > 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error: The following {unavailableModels.Count} model(s) are not available in the registry:[/]");
                    foreach (var model in unavailableModels)
                    {
                        AnsiConsole.MarkupLine($"  [red]• {model}[/]");
                    }
                    AnsiConsole.MarkupLine($"[yellow]These models cannot be pulled. Check the model names are correct.[/]");
                    return 1;
                }

                // If models are missing and on-demand is not enabled, exit
                if (missingModels.Count > 0 && !_args.OnDemand)
                {
                    AnsiConsole.MarkupLine($"[red]Error: The following {missingModels.Count} model(s) are not available:[/]");
                    foreach (var model in missingModels)
                    {
                        AnsiConsole.MarkupLine($"  [red]• {model}[/]");
                    }
                    AnsiConsole.MarkupLine($"[yellow]Make sure the models are pulled, or use --ondemand to pull them automatically.[/]");
                    return 1;
                }

                if (missingModels.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[dim]All models verified[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[dim]All models verified ({missingModels.Count} will be pulled on-demand)[/]");
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

                    // Track whether model was pulled on-demand (for cleanup after testing)
                    // A model should be cleaned up if:
                    // 1. It was verified as missing at startup (will be/was pulled on-demand), OR
                    // 2. It has PulledOnDemand flag set from a previous interrupted run
                    bool pulledOnDemand = existingResult?.PulledOnDemand ?? false;

                    // If model was missing at startup, it needs to be pulled and cleaned up
                    if (_args.OnDemand && modelsVerifiedMissing.Contains(modelFullName))
                    {
                        pulledOnDemand = true;
                    }

                    // Check if model exists on the Ollama instance
                    bool modelExists = await CheckModelExistsAsync(modelFullName);

                    if (!modelExists)
                    {
                        if (_args.OnDemand)
                        {
                            // Pull the model on-demand
                            AnsiConsole.MarkupLine($"[yellow]Model not available, pulling on-demand...[/]");
                            if (!await PullModelAsync(modelFullName))
                            {
                                AnsiConsole.MarkupLine($"[red]Error: Failed to pull model {modelFullName}[/]");
                                continue;
                            }

                            // Resolve actual model name (Ollama may store with different case)
                            var actualModelName = await ResolveActualModelNameAsync(modelFullName);
                            if (actualModelName != null && actualModelName != modelFullName)
                            {
                                AnsiConsole.MarkupLine($"[dim]  Model stored as: {actualModelName}[/]");
                                modelFullName = actualModelName;
                            }

                            pulledOnDemand = true;
                            _modelsPulledThisSession.Add(modelFullName); // Track for reliable cleanup

                            // If resuming, immediately save PulledOnDemand flag to existing result
                            // This ensures cleanup happens even if testing is interrupted again
                            if (existingResult != null)
                            {
                                existingResult.PulledOnDemand = true;
                                SaveResultsFile();
                            }
                        }
                        else
                        {
                            AnsiConsole.MarkupLine($"[red]Error: Model {modelFullName} not found[/]");
                            AnsiConsole.MarkupLine($"[yellow]Make sure the model exists. Try: osync list {_args.ModelName}*[/]");
                            AnsiConsole.MarkupLine($"[yellow]Or use --ondemand to pull models automatically[/]");
                            continue;
                        }
                    }

                    // Get model metadata (pass original tag for fallback quantization extraction)
                    var modelInfo = await GetModelInfoAsync(modelFullName, tag);
                    if (modelInfo == null)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Could not retrieve model info for {modelFullName}[/]");
                        AnsiConsole.MarkupLine($"[yellow]Make sure the model exists. Try: osync list {_args.ModelName}*[/]");
                        // If we pulled on-demand and failed to get info, clean up
                        if (pulledOnDemand)
                        {
                            await DeleteModelAsync(modelFullName);
                        }
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
                    var (preloadSuccess, preloadError) = await PreloadModelAsync(modelFullName);
                    if (!preloadSuccess)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Failed to preload {modelFullName}[/]");
                        if (!string.IsNullOrEmpty(preloadError))
                        {
                            AnsiConsole.MarkupLine($"[red]  {preloadError}[/]");
                        }
                        // Clean up on-demand model if preload fails
                        if (pulledOnDemand || _modelsPulledThisSession.Contains(modelFullName))
                        {
                            await DeleteModelAsync(modelFullName);
                            _modelsPulledThisSession.Remove(modelFullName);
                        }
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
                        quantResult.PulledOnDemand = pulledOnDemand;

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

                        // Clean up on-demand models immediately after testing completes
                        // Judgment doesn't need the model loaded - it only compares saved answers
                        // Use _modelsPulledThisSession as the definitive check for cleanup
                        bool shouldCleanup = pulledOnDemand || _modelsPulledThisSession.Contains(modelFullName);
                        if (shouldCleanup)
                        {
                            await DeleteModelAsync(modelFullName);
                            // Clear the flag since model is already removed
                            quantResult.PulledOnDemand = false;
                            SaveResultsFile();
                        }
                    }
                    else if (pulledOnDemand || _modelsPulledThisSession.Contains(modelFullName))
                    {
                        // Testing failed but model was pulled on-demand, clean it up
                        await DeleteModelAsync(modelFullName);
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

                // Clean up on-demand models after all testing and judgment is complete
                var onDemandModels = _resultsFile.Results.Where(r => r.PulledOnDemand).ToList();
                if (onDemandModels.Count > 0)
                {
                    foreach (var result in onDemandModels)
                    {
                        await DeleteModelAsync(result.ModelName);
                        result.PulledOnDemand = false;
                    }
                    // Save after cleanup to persist the cleared PulledOnDemand flags
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
                await CleanupOnDemandModelsAsync();
                return 2;
            }
            catch (Exception ex) when (ex.InnerException is OperationCanceledException)
            {
                // Cancellation wrapped in another exception (e.g., from Spectre.Console Progress)
                AnsiConsole.MarkupLine($"\n[yellow]Operation cancelled by user[/]");
                SavePartialResults();
                await CleanupOnDemandModelsAsync();
                return 2;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                SavePartialResults();
                await CleanupOnDemandModelsAsync();
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
        /// Clean up on-demand models that were pulled during this session.
        /// Called on failure/cancellation to ensure pulled models don't remain.
        /// </summary>
        private async Task CleanupOnDemandModelsAsync()
        {
            if (!_args.OnDemand || _modelsPulledThisSession.Count == 0)
                return;

            AnsiConsole.MarkupLine($"[dim]Cleaning up {_modelsPulledThisSession.Count} on-demand model(s)...[/]");

            foreach (var modelName in _modelsPulledThisSession.ToList())
            {
                try
                {
                    await DeleteModelAsync(modelName);
                    _modelsPulledThisSession.Remove(modelName);
                    AnsiConsole.MarkupLine($"[dim]  Removed: {modelName}[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]  Warning: Failed to remove {modelName}: {ex.Message}[/]");
                }
            }

            // Also clean up any models marked in the results file
            if (_resultsFile != null)
            {
                var onDemandResults = _resultsFile.Results.Where(r => r.PulledOnDemand).ToList();
                foreach (var result in onDemandResults)
                {
                    if (!_modelsPulledThisSession.Contains(result.ModelName))
                    {
                        try
                        {
                            await DeleteModelAsync(result.ModelName);
                            result.PulledOnDemand = false;
                            AnsiConsole.MarkupLine($"[dim]  Removed: {result.ModelName}[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[yellow]  Warning: Failed to remove {result.ModelName}: {ex.Message}[/]");
                        }
                    }
                }

                // Save to persist cleared PulledOnDemand flags
                if (onDemandResults.Count > 0)
                {
                    SaveResultsFile();
                }
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

            // Display context length settings
            AnsiConsole.MarkupLine($"[dim]Test context length: {_testSuite.ContextLength}[/]");
            _lastDisplayedContextLength = _testSuite.ContextLength;

            if (_judgeEnabled)
            {
                AnsiConsole.MarkupLine($"[dim]Judge context length: {_args.JudgeCtxSize}[/]");
            }
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
        /// <param name="modelName">The model name to query</param>
        /// <param name="originalModelName">The original model name/tag supplied by user (for fallback quantization extraction)</param>
        private async Task<ModelMetadata?> GetModelInfoAsync(string modelName, string? originalModelName = null)
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

                // If quantization type is empty, try to extract from the original model name/tag
                if (string.IsNullOrEmpty(quantizationType))
                {
                    // Try the original model name first (full name with tag)
                    if (!string.IsNullOrEmpty(originalModelName))
                    {
                        quantizationType = ExtractQuantizationFromName(originalModelName);
                    }
                    // Fall back to the modelName if still empty
                    if (string.IsNullOrEmpty(quantizationType))
                    {
                        quantizationType = ExtractQuantizationFromName(modelName);
                    }
                }

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
        /// Returns (success, errorMessage)
        /// </summary>
        private async Task<(bool success, string? error)> PreloadModelAsync(string modelName)
        {
            const int maxRetries = 3;
            string? lastError = null;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    var request = new
                    {
                        model = modelName,
                        messages = new[] { new { role = "user", content = "Hi" } },
                        stream = false
                    };

                    // Use a longer timeout for preload (model loading can take time)
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_args.Timeout));
                    var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/chat", request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        return (true, null);
                    }

                    // Get error details from response
                    var errorContent = await response.Content.ReadAsStringAsync();
                    lastError = $"HTTP {(int)response.StatusCode}: {errorContent}";

                    // Check if it's a retryable error
                    if (response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                        response.StatusCode == System.Net.HttpStatusCode.InternalServerError)
                    {
                        if (attempt < maxRetries)
                        {
                            AnsiConsole.MarkupLine($"[yellow]  Preload attempt {attempt} failed, retrying...[/]");
                            await Task.Delay(2000 * attempt); // Exponential backoff
                            continue;
                        }
                    }

                    return (false, lastError);
                }
                catch (TaskCanceledException)
                {
                    lastError = "Timeout waiting for model to load";
                    if (attempt < maxRetries)
                    {
                        AnsiConsole.MarkupLine($"[yellow]  Preload attempt {attempt} timed out, retrying...[/]");
                        continue;
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastError = $"Connection error: {ex.Message}";
                    if (attempt < maxRetries)
                    {
                        AnsiConsole.MarkupLine($"[yellow]  Preload attempt {attempt} failed ({ex.Message}), retrying...[/]");
                        await Task.Delay(2000 * attempt);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    return (false, lastError);
                }
            }

            return (false, lastError);
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
                ModelName = modelFullName,
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
                ModelName = modelFullName,
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
                // Check for cancellation before each attempt
                if (_cancellationRequested)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                }

                try
                {
                    var result = await operation();
                    if (result != null)
                        return result;

                    // Result was null, will retry
                    lastException = null;
                }
                catch (OperationCanceledException)
                {
                    // Don't retry on cancellation - rethrow immediately
                    throw;
                }
                catch (Exception ex) when (ex.InnerException is OperationCanceledException)
                {
                    // Don't retry on wrapped cancellation - rethrow immediately
                    throw;
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
        /// Resolve context length for a question using hierarchy: question > category > suite
        /// </summary>
        private int ResolveContextLength(TestCategory category, TestQuestion question)
        {
            // Question-level override takes priority
            if (question.ContextLength.HasValue)
                return question.ContextLength.Value;

            // Category-level override is next
            if (category.ContextLength.HasValue)
                return category.ContextLength.Value;

            // Fall back to test suite default
            return _testSuite.ContextLength;
        }

        /// <summary>
        /// Display context length change if different from last displayed
        /// </summary>
        private void DisplayContextLengthIfChanged(int contextLength, TestCategory category, TestQuestion question)
        {
            if (contextLength != _lastDisplayedContextLength)
            {
                string source;
                if (question.ContextLength.HasValue)
                    source = $"question {question.Id}";
                else if (category.ContextLength.HasValue)
                    source = $"category {category.Name}";
                else
                    source = "suite default";

                AnsiConsole.MarkupLine($"[yellow]Context length changed to {contextLength} (from {source})[/]");
                _lastDisplayedContextLength = contextLength;
            }
        }

        /// <summary>
        /// Core implementation for running a single question
        /// </summary>
        private async Task<QuestionResult?> RunSingleQuestionCoreAsync(string modelName, TestCategory category, TestQuestion question)
        {
            // Resolve context length using hierarchy: question > category > suite
            var contextLength = ResolveContextLength(category, question);

            // Display context length change if different from last displayed
            DisplayContextLengthIfChanged(contextLength, category, question);

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
                    NumPredict = _testSuite.NumPredict,
                    NumCtx = contextLength
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
        /// Write verbose judgment output to console with progress indicator
        /// </summary>
        private static readonly object _verboseOutputLock = new object();
        private int _verboseCompletedCount = 0;
        private int _verboseTotalCount = 0;

        private string _verboseCurrentTag = "";

        private void WriteVerboseJudgment(string questionId, JudgmentResult judgment)
        {
            var scoreColor = judgment.Score >= 80 ? "lime" : judgment.Score >= 50 ? "yellow" : "red";

            lock (_verboseOutputLock)
            {
                var completed = Interlocked.Increment(ref _verboseCompletedCount);
                var total = _verboseTotalCount;
                var percent = total > 0 ? (completed * 100 / total) : 0;

                AnsiConsole.Markup($"[magenta]Judging {Markup.Escape(_verboseCurrentTag)}[/] [dim]Q{questionId}[/] Score: [{scoreColor}]{judgment.Score}%[/] [dim]({completed}/{total} {percent}%)[/]");
                AnsiConsole.WriteLine("");

                if (string.IsNullOrWhiteSpace(judgment.Reason))
                {
                    AnsiConsole.MarkupLine("[dim]    (no reason provided)[/]");
                }
                else
                {
                    // Word-wrap the reason into 4 lines at console width (minus indent)
                    var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 120;
                    var lineWidth = consoleWidth - 4; // 4 chars for indent
                    var words = judgment.Reason.Replace("\r", "").Replace("\n", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var lines = new List<string>();
                    var currentLine = new StringBuilder();

                    foreach (var word in words)
                    {
                        if (currentLine.Length + word.Length + 1 > lineWidth)
                        {
                            if (currentLine.Length > 0)
                            {
                                lines.Add(currentLine.ToString());
                                if (lines.Count >= 4) break;
                                currentLine.Clear();
                            }
                        }
                        if (currentLine.Length > 0) currentLine.Append(' ');
                        currentLine.Append(word);
                    }

                    if (currentLine.Length > 0 && lines.Count < 4)
                    {
                        lines.Add(currentLine.ToString());
                    }

                    foreach (var line in lines)
                    {
                        AnsiConsole.MarkupLine($"[dim]    {Markup.Escape(line)}[/]");
                    }
                }
            }
        }

        private void ResetVerboseProgress(int total, string tag)
        {
            _verboseCompletedCount = 0;
            _verboseTotalCount = total;
            _verboseCurrentTag = tag;
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

            // In verbose mode, skip progress bar and just show verbose output with text progress
            if (_args.Verbose)
            {
                // Count questions that need judging
                var questionsToJudge = quantResult.QuestionResults.Count(q =>
                {
                    var baseQ = baseResult.QuestionResults.FirstOrDefault(b => b.QuestionId == q.QuestionId);
                    return baseQ != null && (q.Judgment == null || q.Judgment.JudgeModel != _judgeModelName || _args.Force || _args.Rejudge);
                });
                ResetVerboseProgress(questionsToJudge, quantResult.Tag);

                foreach (var quantQuestion in quantResult.QuestionResults)
                {
                    // Check for cancellation
                    if (_cancellationRequested)
                    {
                        _cts.Token.ThrowIfCancellationRequested();
                    }

                    // Find corresponding base question
                    var baseQuestion = baseResult.QuestionResults
                        .FirstOrDefault(q => q.QuestionId == quantQuestion.QuestionId);

                    if (baseQuestion == null)
                        continue;

                    // Check if already judged with same model
                    if (quantQuestion.Judgment != null &&
                        quantQuestion.Judgment.JudgeModel == _judgeModelName &&
                        !_args.Force && !_args.Rejudge)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Run judgment
                    var judgment = await JudgeQuestionAsync(baseQuestion, quantQuestion);
                    if (judgment != null)
                    {
                        quantQuestion.Judgment = judgment;
                        judgedCount++;
                        WriteVerboseJudgment(quantQuestion.QuestionId, judgment);
                    }
                }

                AnsiConsole.MarkupLine($"[green]✓ Judging {quantResult.Tag} complete[/]");
            }
            else
            {
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
                            // Check for cancellation
                            if (_cancellationRequested)
                            {
                                task.Description = $"[yellow]Cancelled {quantResult.Tag}[/]";
                                _cts.Token.ThrowIfCancellationRequested();
                            }

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
                                !_args.Force && !_args.Rejudge)
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
            }

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
                    !_args.Force && !_args.Rejudge)
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

            // In verbose mode, skip progress bar and just show verbose output with text progress
            if (_args.Verbose)
            {
                ResetVerboseProgress(questionsToJudge.Count, quantResult.Tag);

                var judgmentTasks = questionsToJudge.Select(pair => Task.Run(async () =>
                {
                    var judgment = await JudgeQuestionAsync(pair.baseQuestion, pair.quantQuestion);
                    if (judgment != null)
                    {
                        pair.quantQuestion.Judgment = judgment;
                        WriteVerboseJudgment(pair.quantQuestion.QuestionId, judgment);
                    }
                    Interlocked.Increment(ref completedCount);
                })).ToList();

                await Task.WhenAll(judgmentTasks);
                AnsiConsole.MarkupLine($"[green]✓ Judging {quantResult.Tag} complete[/]");
            }
            else
            {
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
            }

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
        /// Normalizes a score to the 1-100 range.
        /// Scores in 0.0-1.0 range are multiplied by 100.
        /// Scores already in 1-100 range are kept as-is.
        /// </summary>
        private static int NormalizeScoreTo100(double scoreValue)
        {
            // If score is in 0.0-1.0 range, convert to 1-100
            if (scoreValue <= 1.0)
            {
                return (int)Math.Round(scoreValue * 100);
            }
            // Score is already in 1-100+ range, use as-is
            return (int)Math.Round(scoreValue);
        }

        /// <summary>
        /// Attempts to fix truncated JSON by adding missing closing quotes, braces, etc.
        /// Returns the potentially fixed JSON string
        /// </summary>
        private static string TryFixTruncatedJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return json;

            var trimmed = json.Trim();

            // Count quotes, braces, and brackets
            int openBraces = 0, closeBraces = 0;
            int openBrackets = 0, closeBrackets = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\' && inString)
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                }
                else if (!inString)
                {
                    switch (c)
                    {
                        case '{': openBraces++; break;
                        case '}': closeBraces++; break;
                        case '[': openBrackets++; break;
                        case ']': closeBrackets++; break;
                    }
                }
            }

            var result = new StringBuilder(trimmed);

            // If we're still in a string, close it
            if (inString)
            {
                result.Append('"');
            }

            // Add missing closing brackets
            while (closeBrackets < openBrackets)
            {
                result.Append(']');
                closeBrackets++;
            }

            // Add missing closing braces
            while (closeBraces < openBraces)
            {
                result.Append('}');
                closeBraces++;
            }

            return result.ToString();
        }

        /// <summary>
        /// Helper method to get a JSON property case-insensitively
        /// Tries multiple property names and returns the first match
        /// </summary>
        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string[] propertyNames, out JsonElement value)
        {
            value = default;
            foreach (var prop in element.EnumerateObject())
            {
                foreach (var name in propertyNames)
                {
                    if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = prop.Value;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Helper method to extract reason from raw JSON response using multiple strategies
        /// Used as a fallback when normal JSON parsing fails to find the reason field
        /// </summary>
        private static string? TryExtractReasonFromRawResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
                return null;

            // Strategy 1: Try to parse as JSON (with truncation fix) and use case-insensitive property lookup
            string[] jsonVariants = new[] { rawResponse, TryFixTruncatedJson(rawResponse) };
            foreach (var jsonToParse in jsonVariants.Distinct())
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonToParse);
                    if (TryGetPropertyCaseInsensitive(doc.RootElement, new[] { "reason", "response", "explanation" }, out var reasonElement))
                    {
                        var reason = reasonElement.GetString();
                        if (!string.IsNullOrWhiteSpace(reason))
                            return reason;
                    }
                }
                catch
                {
                    // JSON parsing failed, try next variant or fall through to regex
                }
            }

            // Strategy 2: Try multiple regex patterns for maximum compatibility (case-insensitive)
            Match reasonMatch = Match.Empty;

            // Pattern A: Standard JSON format with escaped quotes
            if (!reasonMatch.Success)
                reasonMatch = Regex.Match(rawResponse, @"""(?:reason|response|explanation)""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Pattern B: Simpler pattern - just find the key and capture until closing quote
            if (!reasonMatch.Success)
                reasonMatch = Regex.Match(rawResponse, @"[""']?(?:reason|response|explanation)[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Pattern C: Very lenient - key followed by colon and quoted value (non-greedy)
            if (!reasonMatch.Success)
                reasonMatch = Regex.Match(rawResponse, @"(?:reason|response|explanation)\s*:\s*""(.+?)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            // Pattern D: Truncated JSON - capture everything after opening quote until end (for truncated responses)
            if (!reasonMatch.Success)
                reasonMatch = Regex.Match(rawResponse, @"""(?:reason|response|explanation)""\s*:\s*""(.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);

            if (reasonMatch.Success && reasonMatch.Groups.Count > 1)
            {
                var reason = reasonMatch.Groups[1].Value
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
                if (!string.IsNullOrWhiteSpace(reason))
                    return reason;
            }

            return null;
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

            const int maxReasonRetries = 5;

            for (int attempt = 1; attempt <= maxReasonRetries; attempt++)
            {
                var result = await JudgeQuestionSingleAttemptAsync(baseQuestion, quantQuestion);
                if (result == null)
                    return null;

                // Check if reason is present
                if (!string.IsNullOrWhiteSpace(result.Reason))
                    return result;

                // If reason is empty but we have RawResponse, try regex fallback to extract reason
                if (string.IsNullOrWhiteSpace(result.Reason) && !string.IsNullOrWhiteSpace(result.RawResponse))
                {
                    var extractedReason = TryExtractReasonFromRawResponse(result.RawResponse);
                    if (!string.IsNullOrWhiteSpace(extractedReason))
                    {
                        result.Reason = extractedReason;
                        result.RawResponse = null; // Clear raw response since we extracted the reason
                        return result;
                    }
                }

                // Reason is missing, retry if we have attempts left
                if (attempt < maxReasonRetries)
                {
                    await Task.Delay(500, _cts.Token); // Brief delay before retry
                    continue;
                }

                // After all retries, return result with warning
                AnsiConsole.MarkupLine($"[yellow]Warning: Judge returned score without reason after {maxReasonRetries} attempts for Q{baseQuestion.QuestionId}[/]");
                if (!string.IsNullOrWhiteSpace(result.RawResponse))
                {
                    // Output full raw JSON for debugging (word-wrap at console width)
                    AnsiConsole.MarkupLine("[dim]Raw JSON response:[/]");
                    var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 120;
                    var escaped = Markup.Escape(result.RawResponse);
                    // Word wrap the response for readability
                    for (int i = 0; i < escaped.Length; i += consoleWidth - 4)
                    {
                        var chunk = escaped.Substring(i, Math.Min(consoleWidth - 4, escaped.Length - i));
                        AnsiConsole.MarkupLine($"[dim]    {chunk}[/]");
                    }
                }
                return result;
            }

            return null;
        }

        /// <summary>
        /// Single attempt at judging a question (called by retry wrapper)
        /// </summary>
        private async Task<JudgmentResult?> JudgeQuestionSingleAttemptAsync(QuestionResult baseQuestion, QuestionResult quantQuestion)
        {
            if (_judgeHttpClient == null || _judgeModelName == null || _judgeBaseUrl == null)
                return null;

            // Build the judgment prompt with clear text markers (not JSON - models understand this better)
            var userMessage = $@"{JUDGE_USER_INSTRUCTIONS}

--- QUESTION (for context only) ---
{baseQuestion.Question}
--- END QUESTION ---

--- RESPONSE A ---
{baseQuestion.Answer}
--- END RESPONSE A ---

--- RESPONSE B ---
{quantQuestion.Answer}
--- END RESPONSE B ---

How similar are RESPONSE A and RESPONSE B? Provide score and reason.";

            var request = new
            {
                model = _judgeModelName,
                messages = new object[]
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
                            type = "number",
                            description = "Similarity score: use 1-100 scale (e.g., 85 for 85% similar). Do NOT use 0.0-1.0 scale."
                        },
                        reason = new
                        {
                            type = "string",
                            description = "Comparison explanation starting with 'A and B match:' or 'A and B differ:'"
                        }
                    },
                    required = new[] { "score", "reason" }
                },
                options = new
                {
                    temperature = 0.0,
                    seed = 42,
                    num_predict = 800, // Increased from 200 to avoid truncated JSON responses
                    num_ctx = _args.JudgeCtxSize
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

            var responseText = (contentElement.GetString() ?? "").Trim();

            // Parse the structured JSON response
            int score = 0;
            string reason = "";

            // Try JSON parsing first, then try fixing truncated JSON, then fall back to regex
            bool jsonParsed = false;
            string jsonToParse = responseText;

            // Try parsing twice: first as-is, then with truncation fixes
            for (int parseAttempt = 0; parseAttempt < 2 && !jsonParsed; parseAttempt++)
            {
                if (parseAttempt == 1)
                {
                    // Second attempt: try to fix truncated JSON
                    jsonToParse = TryFixTruncatedJson(responseText);
                    if (jsonToParse == responseText)
                        break; // No changes made, skip second attempt
                }

                try
                {
                    using var scoreDoc = JsonDocument.Parse(jsonToParse);
                    var scoreRoot = scoreDoc.RootElement;
                    jsonParsed = true;

                    // Try "score" field first, then "similarity" as fallback (some models use this)
                    // Use case-insensitive lookup to handle Score, SCORE, score, etc.
                    if (TryGetPropertyCaseInsensitive(scoreRoot, new[] { "score", "similarity" }, out var scoreElement))
                    {
                        if (scoreElement.ValueKind == JsonValueKind.Number)
                        {
                            var scoreValue = scoreElement.GetDouble();
                            // Convert 0.0-1.0 range to 1-100 range before saving
                            // Values <= 1.0 are assumed to be in 0.0-1.0 range (multiply by 100)
                            // Values > 1.0 are assumed to be already in 1-100 range
                            score = NormalizeScoreTo100(scoreValue);
                        }
                    }

                    // Try "reason" field first, then "response" or "explanation" as fallback
                    // Use case-insensitive lookup to handle Reason, REASON, reason, etc.
                    if (TryGetPropertyCaseInsensitive(scoreRoot, new[] { "reason", "response", "explanation" }, out var reasonElement))
                    {
                        reason = reasonElement.GetString() ?? "";
                    }
                }
                catch
                {
                    // JSON parsing failed, will try truncation fix or fall through to regex
                }
            }

            // If score is still 0, try regex fallback (case-insensitive)
            if (score == 0)
            {
                var scoreMatch = Regex.Match(responseText, @"""?(?:score|similarity)""?\s*:\s*(\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
                if (scoreMatch.Success)
                {
                    if (double.TryParse(scoreMatch.Groups[1].Value, out var parsed))
                    {
                        // Convert 0.0-1.0 to 1-100 before saving
                        score = NormalizeScoreTo100(parsed);
                    }
                }
            }

            // ALWAYS try regex fallback for reason if still empty (case-insensitive)
            // This handles cases where JSON parsing found the field but failed to extract it
            if (string.IsNullOrWhiteSpace(reason))
            {
                // Try multiple regex patterns in order of specificity
                Match reasonMatch = Match.Empty;

                // Pattern A: Standard JSON format - "Reason": "value" (handles escaped chars)
                if (!reasonMatch.Success)
                    reasonMatch = Regex.Match(responseText, @"""(?:reason|response|explanation)""\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                // Pattern B: Simpler pattern - just find the key and capture until closing quote
                if (!reasonMatch.Success)
                    reasonMatch = Regex.Match(responseText, @"[""']?(?:reason|response|explanation)[""']?\s*:\s*[""']([^""']+)[""']", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                // Pattern C: Very lenient - key followed by colon and quoted value
                if (!reasonMatch.Success)
                    reasonMatch = Regex.Match(responseText, @"(?:reason|response|explanation)\s*:\s*""(.+?)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                // Pattern D: Truncated JSON - capture everything after opening quote until end (for truncated responses)
                if (!reasonMatch.Success)
                    reasonMatch = Regex.Match(responseText, @"""(?:reason|response|explanation)""\s*:\s*""(.+)$", RegexOptions.Singleline | RegexOptions.IgnoreCase);

                if (reasonMatch.Success && reasonMatch.Groups.Count > 1)
                {
                    reason = reasonMatch.Groups[1].Value
                        .Replace("\\n", "\n")
                        .Replace("\\r", "\r")
                        .Replace("\\\"", "\"")
                        .Replace("\\\\", "\\");
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
                JudgedAt = DateTime.UtcNow,
                RawResponse = string.IsNullOrWhiteSpace(reason) ? responseText : null // Capture raw response for debugging when reason is missing
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
        /// Extract quantization type from model name or tag
        /// Handles patterns like: Q4_K_M, IQ3_XXS, UD-Q6_K_XL, q4_0, etc.
        /// </summary>
        private static string ExtractQuantizationFromName(string modelNameOrTag)
        {
            if (string.IsNullOrEmpty(modelNameOrTag))
                return string.Empty;

            // Common quantization patterns (case insensitive)
            // IQ patterns: IQ1_S, IQ2_XXS, IQ2_XS, IQ2_S, IQ2_M, IQ3_XXS, IQ3_XS, IQ3_S, IQ3_M, IQ4_XS, IQ4_NL
            // Q patterns: Q2_K, Q3_K_S, Q3_K_M, Q3_K_L, Q4_0, Q4_1, Q4_K_S, Q4_K_M, Q5_0, Q5_1, Q5_K_S, Q5_K_M, Q6_K, Q8_0
            // Extended patterns: Q3_K_XL, Q4_K_XL, Q4_K_XXL, Q5_K_XL, Q6_K_XL, etc.
            // May have UD- prefix (Unsloth Dynamic): UD-Q4_K_M, UD-IQ3_XXS

            var patterns = new[]
            {
                // IQ patterns with various suffixes
                @"(?:^|[-_:])?(IQ[1-4]_(?:XXS|XS|S|M|NL|XXL|XL|L))(?:[-_]|$)",
                // Q patterns with K and size suffix
                @"(?:^|[-_:])?(Q[2-8]_K_(?:XXS|XXL|XL|L|M|S))(?:[-_]|$)",
                // Q patterns with just K
                @"(?:^|[-_:])?(Q[2-8]_K)(?:[-_]|$)",
                // Q patterns with number suffix (Q4_0, Q5_1, etc.)
                @"(?:^|[-_:])?(Q[2-8]_[01])(?:[-_]|$)",
                // F16, F32 patterns
                @"(?:^|[-_:])?(F(?:16|32))(?:[-_]|$)",
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    modelNameOrTag,
                    pattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (match.Success && match.Groups.Count > 1)
                {
                    return match.Groups[1].Value.ToUpperInvariant();
                }
            }

            return string.Empty;
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

        /// <summary>
        /// Expands wildcard patterns in quantization tags.
        /// Supports patterns like "*", "Q4*", "IQ*", "hf.co/namespace/repo:Q4*"
        /// </summary>
        private async Task<List<string>> ExpandWildcardTagsAsync(List<string> rawTags)
        {
            var result = new List<string>();
            var resolver = new ModelTagResolver(_httpClient);
            var hasWildcards = rawTags.Any(t => t.Contains('*'));

            if (hasWildcards)
            {
                AnsiConsole.MarkupLine($"[dim]Expanding wildcard patterns...[/]");
            }

            foreach (var rawTag in rawTags)
            {
                if (!rawTag.Contains('*'))
                {
                    // No wildcard, use as-is
                    result.Add(rawTag);
                    continue;
                }

                // Determine model source and tag pattern
                string modelSource;
                string tagPattern;

                if (rawTag.Contains(':'))
                {
                    // Format: "hf.co/namespace/repo:Q4*" or "model:tag*"
                    var colonIndex = rawTag.LastIndexOf(':');
                    modelSource = rawTag.Substring(0, colonIndex);
                    tagPattern = rawTag.Substring(colonIndex + 1);
                }
                else
                {
                    // Format: "Q4*" or "*" - use model name from args
                    modelSource = _args.ModelName;
                    tagPattern = rawTag;
                }

                try
                {
                    var resolvedTags = await resolver.ResolveTagPatternAsync(modelSource, tagPattern, _cts.Token);

                    if (resolvedTags.Count == 0)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Warning: No tags found matching pattern '{rawTag}'[/]");
                        continue;
                    }

                    AnsiConsole.MarkupLine($"[dim]  Pattern '{rawTag}' matched {resolvedTags.Count} tag(s)[/]");

                    foreach (var resolved in resolvedTags)
                    {
                        // Build the full tag reference
                        string fullTag;
                        if (modelSource.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
                        {
                            // HuggingFace model - include full path
                            fullTag = $"{modelSource}:{resolved.Tag}";
                        }
                        else if (modelSource != _args.ModelName)
                        {
                            // Different model source specified - include it
                            fullTag = $"{modelSource}:{resolved.Tag}";
                        }
                        else
                        {
                            // Same as model name - just use tag
                            fullTag = resolved.Tag;
                        }

                        if (!result.Contains(fullTag, StringComparer.OrdinalIgnoreCase))
                        {
                            result.Add(fullTag);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[yellow]Warning: Failed to expand pattern '{rawTag}': {ex.Message}[/]");
                }
            }

            if (hasWildcards && result.Count > 0)
            {
                AnsiConsole.MarkupLine($"[dim]Expanded to {result.Count} quantization(s): {string.Join(", ", result.Take(5))}{(result.Count > 5 ? "..." : "")}[/]");
            }

            return result;
        }

        /// <summary>
        /// Check if a model exists on the Ollama instance
        /// </summary>
        private async Task<bool> CheckModelExistsAsync(string modelName)
        {
            try
            {
                var request = new { name = modelName };
                var response = await _httpClient.PostAsJsonAsync($"{_baseUrl}/api/show", request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Resolve the actual model name as stored by Ollama (case-insensitive lookup).
        /// Ollama may store models with different case than requested (e.g., Q4_0 -> q4_0).
        /// </summary>
        private async Task<string?> ResolveActualModelNameAsync(string requestedName)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("models", out var modelsArray))
                    return null;

                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameElement))
                    {
                        var storedName = nameElement.GetString();
                        if (storedName != null &&
                            string.Equals(storedName, requestedName, StringComparison.OrdinalIgnoreCase))
                        {
                            return storedName;
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

        /// <summary>
        /// Check if a model exists in the Ollama registry (can be pulled)
        /// </summary>
        private async Task<bool> CheckModelExistsInRegistryAsync(string modelName)
        {
            try
            {
                // Parse model name to get registry path
                string modelNameWithoutTag;
                string tag;

                if (modelName.Contains(':'))
                {
                    var parts = modelName.Split(':');
                    modelNameWithoutTag = parts[0];
                    tag = parts[1];
                }
                else
                {
                    modelNameWithoutTag = modelName;
                    tag = "latest";
                }

                // HuggingFace models are pulled directly from HuggingFace, not Ollama registry
                // Check HuggingFace API instead
                if (modelNameWithoutTag.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
                {
                    return await CheckHuggingFaceModelExistsAsync(modelNameWithoutTag, tag);
                }

                // Determine registry path
                string registryPath;
                if (modelNameWithoutTag.Contains('/'))
                {
                    // User namespace model (e.g., "mannix/gemma3")
                    registryPath = modelNameWithoutTag;
                }
                else
                {
                    // Library model (e.g., "llama3") - uses "library" namespace
                    registryPath = $"library/{modelNameWithoutTag}";
                }

                // Try to get the manifest from the registry
                var registryClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                var manifestUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/manifests/{tag}",
                    $"https://registry.ollama.com/v2/{registryPath}/manifests/{tag}"
                };

                foreach (var manifestUrl in manifestUrls)
                {
                    try
                    {
                        var response = await registryClient.GetAsync(manifestUrl, _cts.Token);
                        if (response.IsSuccessStatusCode)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Try next URL
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if a HuggingFace model exists by querying the HuggingFace API
        /// Model format: hf.co/{namespace}/{repo}:{tag}
        /// </summary>
        private async Task<bool> CheckHuggingFaceModelExistsAsync(string modelNameWithoutTag, string tag)
        {
            try
            {
                // Parse hf.co/{namespace}/{repo}
                var path = modelNameWithoutTag.Substring("hf.co/".Length);
                var parts = path.Split('/');
                if (parts.Length < 2)
                    return false;

                var namespacePart = parts[0];
                var repo = parts[1];

                // Check if the repository exists on HuggingFace
                var hfClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                hfClient.DefaultRequestHeaders.Add("User-Agent", "osync");

                // First check if repo exists
                var repoUrl = $"https://huggingface.co/api/models/{namespacePart}/{repo}";
                var repoResponse = await hfClient.GetAsync(repoUrl, _cts.Token);

                if (!repoResponse.IsSuccessStatusCode)
                    return false;

                // Check if the specific GGUF file exists for the tag
                // Common patterns: {repo}-{tag}.gguf, {repo}.{tag}.gguf, {tag}.gguf
                var filesUrl = $"https://huggingface.co/api/models/{namespacePart}/{repo}/tree/main";
                var filesResponse = await hfClient.GetAsync(filesUrl, _cts.Token);

                if (!filesResponse.IsSuccessStatusCode)
                    return true; // Repository exists, assume file is accessible

                var filesJson = await filesResponse.Content.ReadAsStringAsync();
                var tagLower = tag.ToLowerInvariant();

                // Check if any GGUF file matches the tag pattern
                // The tag is typically extracted from filename like: model-Q8_0.gguf -> Q8_0
                return filesJson.Contains($"{tag}.gguf", StringComparison.OrdinalIgnoreCase) ||
                       filesJson.Contains($"-{tag}.gguf", StringComparison.OrdinalIgnoreCase) ||
                       filesJson.Contains($".{tag}.gguf", StringComparison.OrdinalIgnoreCase) ||
                       filesJson.Contains($"_{tag}.gguf", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // On error, assume model might exist to allow pull attempt
                return true;
            }
        }

        /// <summary>
        /// Pull a model from the registry with progress display and retry logic
        /// </summary>
        private async Task<bool> PullModelAsync(string modelName)
        {
            const int maxRetries = 50;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                if (_cancellationRequested)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                }

                try
                {
                    if (attempt == 1)
                    {
                        AnsiConsole.MarkupLine($"[cyan]Pulling model: {modelName}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[yellow]Retry {attempt}/{maxRetries}: Pulling model: {modelName}[/]");
                    }

                    var pullRequest = new
                    {
                        name = modelName,
                        stream = true
                    };

                    var content = new StringContent(
                        JsonSerializer.Serialize(pullRequest),
                        Encoding.UTF8,
                        "application/json"
                    );

                    // Use SendAsync with ResponseHeadersRead to enable true streaming
                    // Without this, HttpClient buffers the entire response before returning
                    var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/pull")
                    {
                        Content = content
                    };

                    var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Failed to pull model (HTTP {response.StatusCode})[/]");
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(2000, _cts.Token);
                            continue;
                        }
                        return false;
                    }

                    using var stream = await response.Content.ReadAsStreamAsync(_cts.Token);
                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 256, leaveOpen: false);

                    string? lastStatus = null;
                    string? currentDigest = null;
                    long totalSize = 0;
                    long completedSize = 0;
                    int lastPercent = -1;
                    bool hasError = false;
                    string? errorMessage = null;

                    while (!reader.EndOfStream)
                    {
                        if (_cancellationRequested)
                        {
                            _cts.Token.ThrowIfCancellationRequested();
                        }

                        var line = await reader.ReadLineAsync();
                        if (string.IsNullOrEmpty(line)) continue;

                        try
                        {
                            using var doc = JsonDocument.Parse(line);
                            var root = doc.RootElement;

                            // Check for error first
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

                            // Get digest if present
                            if (root.TryGetProperty("digest", out var digestElement))
                            {
                                var digest = digestElement.GetString();
                                if (!string.IsNullOrEmpty(digest) && digest != currentDigest)
                                {
                                    currentDigest = digest.Length > 12 ? digest.Substring(0, 12) : digest;
                                }
                            }

                            // Get status
                            if (root.TryGetProperty("status", out var statusElement))
                            {
                                var status = statusElement.GetString();
                                if (!string.IsNullOrEmpty(status) && status != lastStatus)
                                {
                                    // Clear the current line and print new status
                                    if (lastPercent >= 0)
                                    {
                                        // Clear the progress line by overwriting with spaces, then newline
                                        Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
                                        Console.Out.Flush();
                                    }
                                    AnsiConsole.MarkupLine($"[dim]{Markup.Escape(status)}[/]");
                                    lastStatus = status;
                                    lastPercent = -1;
                                    totalSize = 0;
                                    completedSize = 0;
                                    currentDigest = null;
                                }
                            }

                            // Get progress
                            if (root.TryGetProperty("total", out var totalElement))
                            {
                                totalSize = totalElement.GetInt64();
                            }

                            if (root.TryGetProperty("completed", out var completedElement))
                            {
                                completedSize = completedElement.GetInt64();
                                if (totalSize > 0)
                                {
                                    var percent = (int)((completedSize * 100) / totalSize);
                                    if (percent != lastPercent)
                                    {
                                        var sizeInfo = totalSize > 0 ? $" ({FormatBytes(completedSize)}/{FormatBytes(totalSize)})" : "";
                                        var digestInfo = !string.IsNullOrEmpty(currentDigest) ? $" [{currentDigest}]" : "";
                                        var progressLine = $"  Progress: {percent,3}%{sizeInfo}{digestInfo}";
                                        // Pad to clear any previous longer content
                                        var padding = Math.Max(0, 80 - progressLine.Length);
                                        Console.Write($"\r{progressLine}{new string(' ', padding)}");
                                        Console.Out.Flush(); // Ensure progress is displayed immediately
                                        lastPercent = percent;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Ignore JSON parsing errors for individual lines
                        }
                    }

                    // End the progress line if we were showing progress
                    if (lastPercent >= 0)
                    {
                        Console.WriteLine();
                    }

                    if (hasError)
                    {
                        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(errorMessage ?? "Unknown error")}[/]");
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(2000, _cts.Token);
                            continue;
                        }
                        return false;
                    }

                    // Verify the model was actually pulled successfully
                    if (!await CheckModelExistsAsync(modelName))
                    {
                        AnsiConsole.MarkupLine($"[red]Error: Model pull completed but model not found[/]");
                        if (attempt < maxRetries)
                        {
                            await Task.Delay(2000, _cts.Token);
                            continue;
                        }
                        return false;
                    }

                    AnsiConsole.MarkupLine($"[green]✓ Model pulled successfully: {modelName}[/]");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Error pulling model: {ex.Message}[/]");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(2000, _cts.Token);
                        continue;
                    }
                    return false;
                }
            }

            AnsiConsole.MarkupLine($"[red]Failed to pull model after {maxRetries} attempts[/]");
            return false;
        }

        /// <summary>
        /// Format bytes to human-readable string
        /// </summary>
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
            return $"{size:0.#} {sizes[order]}";
        }

        /// <summary>
        /// Delete a model from the Ollama instance
        /// </summary>
        private async Task<bool> DeleteModelAsync(string modelName)
        {
            try
            {
                AnsiConsole.MarkupLine($"[yellow]Removing on-demand model: {modelName}[/]");

                var deleteRequest = new { model = modelName };
                var jsonContent = JsonSerializer.Serialize(deleteRequest);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}/api/delete");
                request.Content = content;

                var response = await _httpClient.SendAsync(request, _cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[green]✓ Model removed: {modelName}[/]");
                    return true;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    AnsiConsole.MarkupLine($"[yellow]Model not found (may have been already removed): {modelName}[/]");
                    return true; // Not an error - model is gone which is what we wanted
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Error: Failed to remove model (HTTP {response.StatusCode})[/]");
                    return false;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error removing model: {ex.Message}[/]");
                return false;
            }
        }
    }
}
