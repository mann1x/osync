using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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

        public QcCommand(QcArgs args, string baseUrl = "http://localhost:11434")
        {
            _args = args;
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
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
        }

        /// <summary>
        /// Main execution method for qc command
        /// </summary>
        public async Task<int> ExecuteAsync()
        {
            try
            {
                // Initialize test suite
                if (!LoadTestSuite())
                    return 1;

                // Initialize or load results file
                if (!await InitializeResultsFileAsync())
                    return 1;

                // Parse quantization tags to test
                var quantTags = _args.Quants.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(q => q.Trim())
                    .ToList();

                if (quantTags.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]Error: No quantization tags specified[/]");
                    return 1;
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

                // Ensure base tag is included if not already present and no base exists yet
                if (existingBase == null && !quantTags.Contains(_args.BaseTag))
                {
                    quantTags.Insert(0, _args.BaseTag);
                }

                // Test each quantization
                foreach (var tag in quantTags)
                {
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

                    // Check if already tested
                    var existingResult = _resultsFile.Results.FirstOrDefault(r => r?.Tag == tagForTracking);
                    if (existingResult != null)
                    {
                        if (!_args.Force)
                        {
                            AnsiConsole.MarkupLine($"[yellow]Skipping {modelFullName} (already tested, use -force to re-run)[/]");
                            continue;
                        }
                        // Remove existing result to re-run
                        _resultsFile.Results.Remove(existingResult);
                        AnsiConsole.MarkupLine($"[yellow]Re-running {modelFullName} (forced)[/]");
                    }

                    AnsiConsole.MarkupLine($"[cyan]Testing quantization: {modelFullName}[/]");

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

                    // Run test suite
                    var quantResult = await RunTestSuiteAsync(modelFullName, tagForTracking, modelInfo);
                    if (quantResult != null)
                    {
                        quantResult.IsBase = (tagForTracking == _args.BaseTag);
                        _resultsFile.Results.Add(quantResult);

                        // Save after each quantization
                        SaveResultsFile();
                        AnsiConsole.MarkupLine($"[green]✓ Completed {modelFullName}[/]");
                    }
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
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error: {ex.Message}[/]");
                return 1;
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
                AnsiConsole.MarkupLine($"[dim]Using test suite: {_testSuite.Name} ({_testSuite.TotalQuestions} questions)[/]");
                return true;
            }

            // Check for internal test suites
            if (_args.TestSuite.Equals("v1quick", StringComparison.OrdinalIgnoreCase))
            {
                _testSuite = new V1QuickTestSuite();
                AnsiConsole.MarkupLine($"[dim]Using test suite: {_testSuite.Name} ({_testSuite.TotalQuestions} questions)[/]");
                return true;
            }

            if (_args.TestSuite.Equals("v1base", StringComparison.OrdinalIgnoreCase))
            {
                _testSuite = new V1BaseTestSuite();
                AnsiConsole.MarkupLine($"[dim]Using test suite: {_testSuite.Name} ({_testSuite.TotalQuestions} questions)[/]");
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
                AnsiConsole.MarkupLine($"[dim]Using external test suite: {_testSuite.Name} ({_testSuite.TotalQuestions} questions)[/]");
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error loading test suite: {ex.Message}[/]");
                return false;
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

            return $"{_args.ModelName}.qc.json";
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
            var categories = _testSuite.GetCategories();
            var totalQuestions = _testSuite.TotalQuestions;
            var questionResults = new List<QuestionResult>();

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
                    var task = ctx.AddTask($"[cyan]Testing {tag}[/]", maxValue: totalQuestions);

                    foreach (var category in categories)
                    {
                        foreach (var question in category.Questions)
                        {
                            task.Description = $"[cyan]{tag}[/] [dim]{category.Name} {question.QuestionId}/{category.Questions.Count}[/]";

                            var result = await RunSingleQuestionAsync(modelFullName, category, question);
                            if (result == null)
                            {
                                // Critical error - cannot continue without logprobs
                                task.StopTask();
                                return;
                            }

                            questionResults.Add(result);
                            task.Increment(1);
                        }
                    }

                    task.Description = $"[green]✓ {tag} complete[/]";
                });

            if (questionResults.Count == 0)
                return null;

            return new QuantResult
            {
                Tag = tag,
                DiskSizeBytes = metadata.SizeBytes,
                Family = metadata.Family,
                ParameterSize = metadata.ParameterSize,
                QuantizationType = metadata.QuantizationType,
                QuestionResults = questionResults
            };
        }

        /// <summary>
        /// Run a single question and capture logprobs
        /// </summary>
        private async Task<QuestionResult?> RunSingleQuestionAsync(string modelName, TestCategory category, TestQuestion question)
        {
            try
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
                        NumPredict = 4096
                    }
                };

                var jsonContent = JsonSerializer.Serialize(request);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content);
                if (!response.IsSuccessStatusCode)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Failed to generate response for question {question.Id}[/]");
                    return null;
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaGenerateResponse>(responseJson);

                if (result == null)
                {
                    AnsiConsole.MarkupLine($"[red]Error: Failed to deserialize response for question {question.Id}[/]");
                    return null;
                }

                // Verify logprobs were received
                if (result.Logprobs == null || result.Logprobs.Count == 0)
                {
                    AnsiConsole.MarkupLine($"[red]Error: No logprobs received for question {question.Id}[/]");
                    AnsiConsole.MarkupLine($"[yellow]Logprobs require Ollama v0.12.11 or later. Please update Ollama.[/]");
                    AnsiConsole.MarkupLine($"[dim]Request JSON:[/]");
                    Console.WriteLine(jsonContent);
                    AnsiConsole.MarkupLine($"[dim]Response JSON:[/]");
                    Console.WriteLine(responseJson);
                    AnsiConsole.MarkupLine($"[yellow]Testing failed - cannot continue without logprobs.[/]");
                    return null;
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
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error testing question {question.Id}: {ex.Message}[/]");
                AnsiConsole.MarkupLine($"[yellow]Testing failed - cannot continue.[/]");
                return null;
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
