using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace osync
{
    public class ChatSession
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly SessionState _state;
        private readonly PerformanceStatistics _stats;
        private bool _isRunning;
        private MultilineMode _multilineMode = MultilineMode.None;
        private readonly StringBuilder _multilineBuffer = new StringBuilder();
        private CancellationTokenSource _cancellationTokenSource = null!;
        private readonly List<string> _commandHistory = new List<string>();
        private int _historyIndex = -1;

        public ChatSession(string baseUrl, string modelName, RunArgs args)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _state = new SessionState
            {
                ModelName = modelName,
                SystemMessage = "",
                WordWrap = !args.NoWordWrap,
                Verbose = args.Verbose,
                HideThinking = args.HideThinking,
                Format = args.Format ?? "",
                Think = ParseThinkArgument(args.Think),
                Truncate = args.Truncate,
                SessionStartTime = DateTime.Now
            };

            // Apply dimension setting to options
            if (args.Dimensions.HasValue)
            {
                _state.Options["dimensions"] = args.Dimensions.Value;
            }

            _client = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl),
                Timeout = Timeout.InfiniteTimeSpan
            };

            _stats = new PerformanceStatistics();

            // Set console encoding for UTF-8 support
            System.Console.OutputEncoding = Encoding.UTF8;
            System.Console.InputEncoding = Encoding.UTF8;
        }

        private object? ParseThinkArgument(string think)
        {
            if (string.IsNullOrEmpty(think)) return null;

            if (bool.TryParse(think, out bool boolValue))
                return boolValue;

            // Return as string for high/medium/low
            return think.ToLower();
        }

        public async Task RunAsync()
        {
            _isRunning = true;
            System.Console.CancelKeyPress += OnCancelKeyPress;

            System.Console.WriteLine($">>> Connecting to {_state.ModelName}...");

            // Preload the model
            await PreloadModelAsync();

            System.Console.WriteLine(">>> Type /? for help or /bye to exit\n");

            while (_isRunning)
            {
                try
                {
                    var prompt = _multilineMode != MultilineMode.None ? "... " : ">>> ";
                    System.Console.Write(prompt);

                    var input = ReadLineWithShortcuts();

                    if (input == null) // Ctrl+D on empty line
                    {
                        break;
                    }

                    if (!await ProcessInputAsync(input))
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"\nError: {ex.Message}");
                }
            }

            System.Console.CancelKeyPress -= OnCancelKeyPress;
        }

        private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                e.Cancel = true; // Don't terminate the application
                System.Console.WriteLine("\n^C");
                _cancellationTokenSource.Cancel();
            }
        }

        private async Task<bool> ProcessInputAsync(string input)
        {
            var trimmedInput = input.Trim();

            // Handle multiline mode
            if (_multilineMode != MultilineMode.None)
            {
                // Check if line ends with """ (end multiline mode)
                if (trimmedInput.EndsWith("\"\"\""))
                {
                    // Add the content before the """ to buffer
                    var contentBeforeQuotes = trimmedInput.Substring(0, trimmedInput.Length - 3).Trim();
                    if (!string.IsNullOrEmpty(contentBeforeQuotes))
                    {
                        _multilineBuffer.AppendLine(contentBeforeQuotes);
                    }
                    return await CompleteMultilineInputAsync();
                }
                else
                {
                    _multilineBuffer.AppendLine(input);
                    return true;
                }
            }

            // Check for multiline start - if line starts with """
            if (trimmedInput.StartsWith("\"\"\""))
            {
                _multilineMode = MultilineMode.UserMessage;
                _multilineBuffer.Clear();

                // Get content after the opening """
                var contentAfterQuotes = trimmedInput.Substring(3).Trim();
                if (!string.IsNullOrEmpty(contentAfterQuotes))
                {
                    _multilineBuffer.AppendLine(contentAfterQuotes);
                }

                System.Console.WriteLine(">>> [Entering multiline mode - type your message across multiple lines]");
                System.Console.WriteLine(">>> [End with \"\"\" on a new line or at end of line to send]");
                return true;
            }

            // Add to history if not empty
            if (!string.IsNullOrWhiteSpace(input))
            {
                _commandHistory.Add(input);
                _historyIndex = _commandHistory.Count; // Reset to end
            }

            // Process commands
            if (input.StartsWith("/"))
            {
                return await HandleCommandAsync(input);
            }

            // Regular chat message
            if (!string.IsNullOrWhiteSpace(input))
            {
                return await SendMessageAsync(input);
            }

            return true;
        }

        private async Task<bool> CompleteMultilineInputAsync()
        {
            var content = _multilineBuffer.ToString().Trim();
            _multilineMode = MultilineMode.None;
            _multilineBuffer.Clear();

            if (string.IsNullOrWhiteSpace(content))
            {
                System.Console.WriteLine(">>> [Multiline input cancelled - no content]");
                return true;
            }

            System.Console.WriteLine(">>> [Sending multiline message...]");
            return await SendMessageAsync(content);
        }

        private async Task<bool> HandleCommandAsync(string input)
        {
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var command = parts[0].ToLower();
            var args = parts.Skip(1).ToArray();

            switch (command)
            {
                case "/bye":
                case "/exit":
                    return false;

                case "/?":
                case "/help":
                    ShowHelp(args.Length > 0 ? args[0] : string.Empty);
                    break;

                case "/set":
                    await HandleSetCommandAsync(args);
                    break;

                case "/show":
                    await HandleShowCommandAsync(args);
                    break;

                case "/clear":
                    ClearContext();
                    break;

                case "/save":
                    await SaveSessionAsync(args);
                    break;

                case "/load":
                    await LoadSessionAsync(args);
                    break;

                case "/stats":
                    _stats.Display();
                    break;

                default:
                    System.Console.WriteLine($"Unknown command: {command}");
                    System.Console.WriteLine("Type /? for help");
                    break;
            }

            return true;
        }

        private async Task HandleSetCommandAsync(string[] args)
        {
            if (args.Length == 0)
            {
                System.Console.WriteLine("Usage: /set <option> [value]");
                System.Console.WriteLine("Type /help set for more information");
                return;
            }

            var option = args[0].ToLower();

            switch (option)
            {
                case "verbose":
                    _state.Verbose = true;
                    System.Console.WriteLine("Verbose mode enabled");
                    break;

                case "quiet":
                    _state.Verbose = false;
                    System.Console.WriteLine("Verbose mode disabled");
                    break;

                case "wordwrap":
                    _state.WordWrap = true;
                    System.Console.WriteLine("Word wrap enabled");
                    break;

                case "nowordwrap":
                    _state.WordWrap = false;
                    System.Console.WriteLine("Word wrap disabled");
                    break;

                case "format":
                    if (args.Length < 2)
                    {
                        System.Console.WriteLine("Usage: /set format <format>");
                        return;
                    }
                    _state.Format = args[1];
                    System.Console.WriteLine($"Format set to: {_state.Format}");
                    break;

                case "noformat":
                    _state.Format = null;
                    System.Console.WriteLine("Format cleared");
                    break;

                case "think":
                    if (args.Length < 2)
                    {
                        _state.Think = true;
                        System.Console.WriteLine("Thinking mode enabled");
                    }
                    else
                    {
                        var thinkValue = args[1].ToLower();
                        if (bool.TryParse(thinkValue, out bool boolVal))
                        {
                            _state.Think = boolVal;
                        }
                        else
                        {
                            _state.Think = thinkValue; // high/medium/low
                        }
                        System.Console.WriteLine($"Thinking mode set to: {_state.Think}");
                    }
                    break;

                case "nothink":
                    _state.Think = null;
                    System.Console.WriteLine("Thinking mode disabled");
                    break;

                case "system":
                    System.Console.WriteLine("Enter system message (end with \"\"\" on new line):");
                    _multilineMode = MultilineMode.SystemMessage;
                    _multilineBuffer.Clear();
                    break;

                case "parameter":
                    if (args.Length < 3)
                    {
                        System.Console.WriteLine("Usage: /set parameter <name> <value>");
                        return;
                    }
                    var paramName = args[1];
                    var paramValue = args[2];

                    // Try to parse as number
                    if (double.TryParse(paramValue, out double doubleVal))
                    {
                        _state.Options[paramName] = doubleVal;
                    }
                    else if (int.TryParse(paramValue, out int intVal))
                    {
                        _state.Options[paramName] = intVal;
                    }
                    else
                    {
                        _state.Options[paramName] = paramValue;
                    }
                    System.Console.WriteLine($"Parameter {paramName} set to: {paramValue}");
                    break;

                default:
                    System.Console.WriteLine($"Unknown option: {option}");
                    System.Console.WriteLine("Type /help set for more information");
                    break;
            }
        }

        private async Task HandleShowCommandAsync(string[] args)
        {
            var what = args.Length > 0 ? args[0].ToLower() : "all";

            try
            {
                switch (what)
                {
                    case "all":
                    case "info":
                        var infoResponse = await _client.PostAsJsonAsync("/api/show",
                            new { name = _state.ModelName });
                        if (infoResponse.IsSuccessStatusCode)
                        {
                            var infoJson = await infoResponse.Content.ReadAsStringAsync();
                            System.Console.WriteLine(infoJson);
                        }
                        break;

                    case "license":
                        var licenseResponse = await _client.PostAsJsonAsync("/api/show",
                            new { name = _state.ModelName });
                        if (licenseResponse.IsSuccessStatusCode)
                        {
                            var licenseJson = await licenseResponse.Content.ReadAsStringAsync();
                            var doc = JsonDocument.Parse(licenseJson);
                            if (doc.RootElement.TryGetProperty("license", out var license))
                            {
                                System.Console.WriteLine(license.GetString());
                            }
                        }
                        break;

                    case "modelfile":
                        var modelfileResponse = await _client.PostAsJsonAsync("/api/show",
                            new { name = _state.ModelName, verbose = true });
                        if (modelfileResponse.IsSuccessStatusCode)
                        {
                            var modelfileJson = await modelfileResponse.Content.ReadAsStringAsync();
                            var doc = JsonDocument.Parse(modelfileJson);
                            if (doc.RootElement.TryGetProperty("modelfile", out var modelfile))
                            {
                                System.Console.WriteLine(modelfile.GetString());
                            }
                        }
                        break;

                    case "parameters":
                        System.Console.WriteLine("\nModel parameters:");
                        foreach (var param in _state.Options)
                        {
                            System.Console.WriteLine($"  {param.Key}: {param.Value}");
                        }
                        break;

                    case "system":
                        if (!string.IsNullOrEmpty(_state.SystemMessage))
                        {
                            System.Console.WriteLine($"System message:\n{_state.SystemMessage}");
                        }
                        else
                        {
                            System.Console.WriteLine("No system message set");
                        }
                        break;

                    case "template":
                        var templateResponse = await _client.PostAsJsonAsync("/api/show",
                            new { name = _state.ModelName });
                        if (templateResponse.IsSuccessStatusCode)
                        {
                            var templateJson = await templateResponse.Content.ReadAsStringAsync();
                            var doc = JsonDocument.Parse(templateJson);
                            if (doc.RootElement.TryGetProperty("template", out var template))
                            {
                                System.Console.WriteLine(template.GetString());
                            }
                        }
                        else
                        {
                            System.Console.WriteLine("No template available");
                        }
                        break;

                    default:
                        System.Console.WriteLine($"Unknown show option: {what}");
                        System.Console.WriteLine("Available: info, license, modelfile, parameters, system, template");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error showing model info: {ex.Message}");
            }
        }

        private void ClearContext()
        {
            // Keep system message but clear conversation
            _state.Messages.Clear();
            System.Console.WriteLine("Context cleared");
        }

        private async Task SaveSessionAsync(string[] args)
        {
            string filePath;

            if (args.Length > 0)
            {
                filePath = string.Join(" ", args);
            }
            else
            {
                System.Console.Write("Enter file path to save session: ");
                filePath = System.Console.ReadLine() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                System.Console.WriteLine("Save cancelled");
                return;
            }

            try
            {
                var json = _state.ToJson();
                await File.WriteAllTextAsync(filePath, json);
                System.Console.WriteLine($"Session saved to: {filePath}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error saving session: {ex.Message}");
            }
        }

        private async Task LoadSessionAsync(string[] args)
        {
            string filePath;

            if (args.Length > 0)
            {
                filePath = string.Join(" ", args);
            }
            else
            {
                System.Console.Write("Enter file path to load session: ");
                filePath = System.Console.ReadLine() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(filePath))
            {
                System.Console.WriteLine("Load cancelled");
                return;
            }

            if (!File.Exists(filePath))
            {
                System.Console.WriteLine($"File not found: {filePath}");
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var loadedState = SessionState.FromJson(json);

                if (loadedState == null)
                {
                    System.Console.WriteLine("Error: Failed to parse session file");
                    return;
                }

                // Merge loaded state
                _state.ModelName = loadedState.ModelName;
                _state.SystemMessage = loadedState.SystemMessage;
                _state.Messages = loadedState.Messages;
                _state.Options = loadedState.Options;
                _state.WordWrap = loadedState.WordWrap;
                _state.Verbose = loadedState.Verbose;
                _state.HideThinking = loadedState.HideThinking;
                _state.Format = loadedState.Format;
                _state.Think = loadedState.Think;
                _state.Truncate = loadedState.Truncate;

                System.Console.WriteLine($"Session loaded from: {filePath}");
                System.Console.WriteLine($"Model: {_state.ModelName}");
                System.Console.WriteLine($"Messages: {_state.Messages.Count}");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"Error loading session: {ex.Message}");
            }
        }

        #region Help System

        private void ShowHelp(string topic)
        {
            if (string.IsNullOrEmpty(topic))
            {
                System.Console.WriteLine("");
                System.Console.WriteLine("Available Commands:");
                System.Console.WriteLine("  /set            Set session variables");
                System.Console.WriteLine("  /show           Show model information");
                System.Console.WriteLine("  /clear          Clear session context");
                System.Console.WriteLine("  /bye            Exit");
                System.Console.WriteLine("  /?, /help       Help for a command");
                System.Console.WriteLine("  /load <path>    Load a session from file");
                System.Console.WriteLine("  /save <path>    Save current session to file");
                System.Console.WriteLine("  /stats          Display performance statistics");
                System.Console.WriteLine("");
                System.Console.WriteLine("Use /help <command> for more information on a specific command.");
                System.Console.WriteLine("Use /help shortcuts for keyboard shortcuts.");
                System.Console.WriteLine("");
            }
            else if (topic == "shortcuts")
            {
                System.Console.WriteLine("");
                System.Console.WriteLine("Keyboard Shortcuts:");
                System.Console.WriteLine("  Ctrl + a / Home    Move to beginning of line");
                System.Console.WriteLine("  Ctrl + e / End     Move to end of line");
                System.Console.WriteLine("  Ctrl + k           Delete from cursor to end of line");
                System.Console.WriteLine("  Ctrl + u           Delete from beginning to cursor");
                System.Console.WriteLine("  Ctrl + w           Delete word before cursor");
                System.Console.WriteLine("  Ctrl + l           Clear screen");
                System.Console.WriteLine("  Ctrl + c           Cancel current generation");
                System.Console.WriteLine("  Ctrl + d           Exit (when line is empty)");
                System.Console.WriteLine("  Up / Down arrows   Navigate command history");
                System.Console.WriteLine("");
                System.Console.WriteLine("Multiline Input:");
                System.Console.WriteLine("  Type \"\"\" (triple quotes) to begin multiline message");
                System.Console.WriteLine("  - Can be alone or followed by text: \"\"\" or \"\"\"your message");
                System.Console.WriteLine("  Type \"\"\" to end multiline message");
                System.Console.WriteLine("  - Can be alone or at end of text: \"\"\" or your message\"\"\"");
                System.Console.WriteLine("");
            }
            else if (topic == "set")
            {
                ShowSetHelp();
            }
            else if (topic == "show")
            {
                System.Console.WriteLine("");
                System.Console.WriteLine("/show command usage:");
                System.Console.WriteLine("  /show                Show all model information");
                System.Console.WriteLine("  /show info           Show model details");
                System.Console.WriteLine("  /show license        Show model license");
                System.Console.WriteLine("  /show modelfile      Show Modelfile");
                System.Console.WriteLine("  /show parameters     Show model and user parameters");
                System.Console.WriteLine("  /show system         Show system message");
                System.Console.WriteLine("  /show template       Show prompt template");
                System.Console.WriteLine("");
            }
            else
            {
                System.Console.WriteLine($"No help available for: {topic}");
            }
        }

        private void ShowSetHelp()
        {
            System.Console.WriteLine("");
            System.Console.WriteLine("/set command usage:");
            System.Console.WriteLine("  /set verbose              Enable verbose output with timings");
            System.Console.WriteLine("  /set quiet                Disable verbose output");
            System.Console.WriteLine("  /set wordwrap             Enable word wrapping");
            System.Console.WriteLine("  /set nowordwrap           Disable word wrapping");
            System.Console.WriteLine("  /set format <format>      Set response format (e.g., json)");
            System.Console.WriteLine("  /set noformat             Clear response format");
            System.Console.WriteLine("  /set think [level]        Enable thinking mode (true/false/high/medium/low)");
            System.Console.WriteLine("  /set nothink              Disable thinking mode");
            System.Console.WriteLine("  /set system               Set system message (starts multiline input)");
            System.Console.WriteLine("  /set parameter <n> <v>    Set model parameter (e.g., temperature 0.8)");
            System.Console.WriteLine("");
            System.Console.WriteLine("Common parameters:");
            System.Console.WriteLine("  temperature              Randomness (0.0-2.0, default: 0.8)");
            System.Console.WriteLine("  top_p                    Nucleus sampling (0.0-1.0)");
            System.Console.WriteLine("  top_k                    Top-k sampling (integer)");
            System.Console.WriteLine("  num_ctx                  Context window size");
            System.Console.WriteLine("  repeat_penalty           Repetition penalty (default: 1.1)");
            System.Console.WriteLine("  num_predict              Max tokens to generate");
            System.Console.WriteLine("");
        }

        #endregion

        #region Keyboard Input

        private string? ReadLineWithShortcuts()
        {
            var buffer = new StringBuilder();
            int cursorPosition = 0;

            while (true)
            {
                var key = System.Console.ReadKey(intercept: true);

                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        System.Console.WriteLine("");
                        return buffer.ToString();

                    case ConsoleKey.D when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        if (buffer.Length == 0)
                        {
                            System.Console.WriteLine(""); // Move to new line before exit
                            return null; // Exit signal
                        }
                        // If buffer is not empty, don't insert the Ctrl+D character
                        break;

                    case ConsoleKey.C when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        // Already handled by CancelKeyPress event
                        break;

                    case ConsoleKey.L when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        System.Console.Clear();
                        var prompt = _multilineMode != MultilineMode.None ? "... " : ">>> ";
                        System.Console.Write(prompt);
                        System.Console.Write(buffer.ToString());
                        cursorPosition = buffer.Length;
                        break;

                    case ConsoleKey.A when (key.Modifiers & ConsoleModifiers.Control) != 0:
                    case ConsoleKey.Home:
                        MoveCursorToPosition(0, cursorPosition, buffer.ToString());
                        cursorPosition = 0;
                        break;

                    case ConsoleKey.E when (key.Modifiers & ConsoleModifiers.Control) != 0:
                    case ConsoleKey.End:
                        MoveCursorToPosition(buffer.Length, cursorPosition, buffer.ToString());
                        cursorPosition = buffer.Length;
                        break;

                    case ConsoleKey.K when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        buffer.Remove(cursorPosition, buffer.Length - cursorPosition);
                        ClearToEndOfLine();
                        break;

                    case ConsoleKey.U when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        buffer.Remove(0, cursorPosition);
                        var prompt2 = _multilineMode != MultilineMode.None ? "... " : ">>> ";
                        System.Console.Write($"\r{prompt2}");
                        System.Console.Write(buffer.ToString());
                        ClearToEndOfLine();
                        cursorPosition = 0;
                        MoveCursorToPosition(0, buffer.Length, buffer.ToString());
                        break;

                    case ConsoleKey.W when (key.Modifiers & ConsoleModifiers.Control) != 0:
                        DeleteWordBackward(buffer, ref cursorPosition);
                        break;

                    case ConsoleKey.Backspace:
                        if (cursorPosition > 0)
                        {
                            buffer.Remove(cursorPosition - 1, 1);
                            cursorPosition--;
                            RedrawLine(buffer.ToString(), cursorPosition);
                        }
                        break;

                    case ConsoleKey.Delete:
                        if (cursorPosition < buffer.Length)
                        {
                            buffer.Remove(cursorPosition, 1);
                            RedrawLine(buffer.ToString(), cursorPosition);
                        }
                        break;

                    case ConsoleKey.LeftArrow:
                        if (cursorPosition > 0)
                        {
                            cursorPosition--;
                            System.Console.SetCursorPosition(System.Console.CursorLeft - 1, System.Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.RightArrow:
                        if (cursorPosition < buffer.Length)
                        {
                            cursorPosition++;
                            System.Console.SetCursorPosition(System.Console.CursorLeft + 1, System.Console.CursorTop);
                        }
                        break;

                    case ConsoleKey.UpArrow:
                        if (_commandHistory.Count > 0 && _historyIndex > 0)
                        {
                            _historyIndex--;
                            buffer.Clear();
                            buffer.Append(_commandHistory[_historyIndex]);
                            cursorPosition = buffer.Length;
                            RedrawLine(buffer.ToString(), cursorPosition);
                        }
                        break;

                    case ConsoleKey.DownArrow:
                        if (_commandHistory.Count > 0)
                        {
                            if (_historyIndex < _commandHistory.Count - 1)
                            {
                                _historyIndex++;
                                buffer.Clear();
                                buffer.Append(_commandHistory[_historyIndex]);
                                cursorPosition = buffer.Length;
                                RedrawLine(buffer.ToString(), cursorPosition);
                            }
                            else if (_historyIndex == _commandHistory.Count - 1)
                            {
                                // Move past end - clear buffer
                                _historyIndex = _commandHistory.Count;
                                buffer.Clear();
                                cursorPosition = 0;
                                RedrawLine(buffer.ToString(), cursorPosition);
                            }
                        }
                        break;

                    default:
                        // Filter out control characters (including Ctrl+D which produces ASCII 4)
                        if (!char.IsControl(key.KeyChar))
                        {
                            buffer.Insert(cursorPosition, key.KeyChar);
                            cursorPosition++;

                            if (cursorPosition == buffer.Length)
                            {
                                System.Console.Write(key.KeyChar);
                            }
                            else
                            {
                                RedrawLine(buffer.ToString(), cursorPosition);
                            }
                        }
                        // Silently ignore all control characters that aren't handled above
                        break;
                }
            }
        }

        private void MoveCursorToPosition(int target, int current, string line)
        {
            var promptLen = _multilineMode != MultilineMode.None ? 4 : 4; // ">>> " or "... "
            var targetAbsolute = promptLen + target;

            System.Console.SetCursorPosition(targetAbsolute, System.Console.CursorTop);
        }

        private void RedrawLine(string line, int cursorPos)
        {
            var currentTop = System.Console.CursorTop;
            var promptLen = _multilineMode != MultilineMode.None ? 4 : 4;

            System.Console.SetCursorPosition(promptLen, currentTop);
            System.Console.Write(line);
            ClearToEndOfLine();
            System.Console.SetCursorPosition(promptLen + cursorPos, currentTop);
        }

        private void ClearToEndOfLine()
        {
            var currentLeft = System.Console.CursorLeft;
            var currentTop = System.Console.CursorTop;
            var width = System.Console.WindowWidth;

            if (currentLeft < width)
            {
                System.Console.Write(new string(' ', width - currentLeft));
                System.Console.SetCursorPosition(currentLeft, currentTop);
            }
        }

        private void DeleteWordBackward(StringBuilder buffer, ref int cursorPosition)
        {
            if (cursorPosition == 0) return;

            int deleteStart = cursorPosition - 1;

            // Skip trailing whitespace
            while (deleteStart >= 0 && char.IsWhiteSpace(buffer[deleteStart]))
            {
                deleteStart--;
            }

            // Delete word characters
            while (deleteStart >= 0 && !char.IsWhiteSpace(buffer[deleteStart]))
            {
                deleteStart--;
            }

            deleteStart++; // Move back to first char to delete

            int deleteCount = cursorPosition - deleteStart;
            if (deleteCount > 0)
            {
                buffer.Remove(deleteStart, deleteCount);
                cursorPosition = deleteStart;

                RedrawLine(buffer.ToString(), cursorPosition);
            }
        }

        #endregion

        #region Model Preloading

        private async Task PreloadModelAsync()
        {
            try
            {
                System.Console.WriteLine(">>> Loading model into memory...");

                // Send empty chat request to preload the model
                var preloadRequest = new ChatRequest
                {
                    Model = _state.ModelName
                };

                var jsonContent = JsonSerializer.Serialize(preloadRequest);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                {
                    Content = httpContent
                };

                var response = await _client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // Consume the response to complete the preload
                await response.Content.ReadAsStringAsync();

                // Now get the process status
                await DisplayProcessStatusAsync();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($">>> Warning: Could not preload model: {ex.Message}");
            }
        }

        private async Task DisplayProcessStatusAsync()
        {
            try
            {
                var response = await _client.GetAsync("/api/ps");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                if (status?.Models == null || status.Models.Count == 0)
                {
                    System.Console.WriteLine(">>> No models currently loaded in memory");
                    return;
                }

                System.Console.WriteLine("");
                System.Console.WriteLine("Loaded Models:");
                System.Console.WriteLine(new string('-', 135));
                System.Console.WriteLine($"{"NAME",-30} {"ID",-15} {"SIZE",-25} {"VRAM USAGE",-15} {"CONTEXT",-10} {"UNTIL",-30}");
                System.Console.WriteLine(new string('-', 135));

                foreach (var model in status.Models)
                {
                    var name = TruncateString(model.Name, 30);
                    var id = GetShortDigest(model.Digest);
                    var size = FormatModelSize(model.Size, model.Details?.ParameterSize);
                    var vramUsage = FormatBytes(model.SizeVram);
                    var context = model.ContextLength > 0 ? model.ContextLength.ToString() : "N/A";
                    var until = FormatUntil(model.ExpiresAt);

                    System.Console.WriteLine($"{name,-30} {id,-15} {size,-25} {vramUsage,-15} {context,-10} {until,-30}");
                }

                System.Console.WriteLine(new string('-', 135));
                System.Console.WriteLine("");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($">>> Warning: Could not retrieve process status: {ex.Message}");
            }
        }

        private string FormatModelSize(long sizeBytes, string? parameterSize)
        {
            var diskSize = FormatBytes(sizeBytes);
            if (!string.IsNullOrEmpty(parameterSize))
            {
                return $"{diskSize} ({parameterSize})";
            }
            return diskSize;
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string FormatUntil(DateTime expiresAt)
        {
            var now = DateTime.Now;
            var timeSpan = expiresAt - now;

            if (timeSpan.TotalMinutes < 1)
            {
                return "Less than a minute";
            }
            else if (timeSpan.TotalMinutes < 2)
            {
                return "About a minute from now";
            }
            else if (timeSpan.TotalMinutes < 60)
            {
                return $"{(int)timeSpan.TotalMinutes} minutes from now";
            }
            else if (timeSpan.TotalHours < 2)
            {
                return "About an hour from now";
            }
            else if (timeSpan.TotalHours < 24)
            {
                return $"{(int)timeSpan.TotalHours} hours from now";
            }
            else
            {
                return $"{(int)timeSpan.TotalDays} days from now";
            }
        }

        private string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str ?? "";

            return str.Substring(0, maxLength - 3) + "...";
        }

        private string GetShortDigest(string digest)
        {
            if (string.IsNullOrEmpty(digest))
                return "N/A";

            // Return first 12 characters of digest (like Docker)
            return digest.Length >= 12 ? digest.Substring(0, 12) : digest;
        }

        #endregion

        #region Message Handling

        private async Task<bool> SendMessageAsync(string content)
        {
            // Add user message to history
            _state.Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = content
            });

            // Build request
            var request = new ChatRequest
            {
                Model = _state.ModelName,
                Messages = BuildMessages(),
                Stream = true,
                Format = !string.IsNullOrEmpty(_state.Format) ? _state.Format : null,
                Options = _state.Options.Count > 0 ? _state.Options : null,
                Think = _state.Think,
                Truncate = _state.Truncate
            };

            try
            {
                _cancellationTokenSource = new CancellationTokenSource();

                var jsonContent = JsonSerializer.Serialize(request);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                {
                    Content = httpContent
                };

                var response = await _client.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                response.EnsureSuccessStatusCode();

                await ProcessStreamingResponseAsync(response);
            }
            catch (OperationCanceledException)
            {
                System.Console.WriteLine("\n>>> Generation cancelled");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"\nError: {ex.Message}");
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null!;
            }

            return true;
        }

        private List<ChatMessage> BuildMessages()
        {
            var messages = new List<ChatMessage>();

            // Add system message if set
            if (!string.IsNullOrEmpty(_state.SystemMessage))
            {
                messages.Add(new ChatMessage
                {
                    Role = "system",
                    Content = _state.SystemMessage
                });
            }

            // Add conversation history
            messages.AddRange(_state.Messages);

            return messages;
        }

        private async Task ProcessStreamingResponseAsync(HttpResponseMessage response)
        {
            var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var assistantMessage = new StringBuilder();
            var thinkingMessage = new StringBuilder();
            bool isThinking = false;
            ChatResponse? lastResponse = null;

            while (!reader.EndOfStream && !_cancellationTokenSource.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var chunk = JsonSerializer.Deserialize<ChatResponse>(line);
                    if (chunk == null) continue;

                    // Handle thinking output
                    if (!string.IsNullOrEmpty(chunk.Message?.Thinking))
                    {
                        if (!isThinking && !_state.HideThinking)
                        {
                            System.Console.Write("\n[Thinking");
                            isThinking = true;
                        }
                        thinkingMessage.Append(chunk.Message.Thinking);
                        if (!_state.HideThinking)
                        {
                            System.Console.Write(".");
                        }
                    }

                    // Handle regular content
                    if (!string.IsNullOrEmpty(chunk.Message?.Content))
                    {
                        if (isThinking && !_state.HideThinking)
                        {
                            System.Console.WriteLine("]\n");
                            isThinking = false;
                        }

                        var text = chunk.Message.Content;
                        assistantMessage.Append(text);

                        // Output text with proper newline handling
                        if (_state.WordWrap)
                        {
                            WriteWithWordWrap(text);
                        }
                        else
                        {
                            System.Console.Write(text);
                        }
                    }

                    if (chunk.Done)
                    {
                        if (isThinking && !_state.HideThinking)
                        {
                            System.Console.WriteLine("]");
                        }
                        lastResponse = chunk;
                        break;
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed JSON lines
                    continue;
                }
            }

            System.Console.WriteLine(""); // Final newline

            // Store assistant message in history
            if (assistantMessage.Length > 0)
            {
                _state.Messages.Add(new ChatMessage
                {
                    Role = "assistant",
                    Content = assistantMessage.ToString(),
                    Thinking = thinkingMessage.Length > 0 ? thinkingMessage.ToString() : null
                });
            }

            // Display verbose stats if enabled
            if (_state.Verbose && lastResponse != null)
            {
                DisplayVerboseStats(lastResponse);
            }

            // Track performance statistics
            if (lastResponse != null)
            {
                _stats.AddResponse(lastResponse);
            }
        }

        private void WriteWithWordWrap(string text)
        {
            var consoleWidth = System.Console.WindowWidth;
            var currentColumn = System.Console.CursorLeft;

            foreach (var ch in text)
            {
                if (ch == '\n')
                {
                    System.Console.WriteLine("");
                    currentColumn = 0;
                }
                else if (ch == '\r')
                {
                    // Skip carriage returns
                    continue;
                }
                else
                {
                    System.Console.Write(ch);
                    currentColumn++;

                    // Wrap at console width
                    if (currentColumn >= consoleWidth - 1)
                    {
                        System.Console.WriteLine("");
                        currentColumn = 0;
                    }
                }
            }
        }

        private void DisplayVerboseStats(ChatResponse response)
        {
            System.Console.WriteLine("");

            var totalDuration = response.TotalDuration.HasValue
                ? TimeSpan.FromMilliseconds(response.TotalDuration.Value / 1_000_000.0)
                : TimeSpan.Zero;

            var loadDuration = response.LoadDuration.HasValue
                ? TimeSpan.FromMilliseconds(response.LoadDuration.Value / 1_000_000.0)
                : TimeSpan.Zero;

            var promptEvalDuration = response.PromptEvalDuration.HasValue
                ? TimeSpan.FromMilliseconds(response.PromptEvalDuration.Value / 1_000_000.0)
                : TimeSpan.Zero;

            var evalDuration = response.EvalDuration.HasValue
                ? TimeSpan.FromMilliseconds(response.EvalDuration.Value / 1_000_000.0)
                : TimeSpan.Zero;

            System.Console.WriteLine($"total duration:       {totalDuration.TotalSeconds:F2}s");
            System.Console.WriteLine($"load duration:        {loadDuration.TotalSeconds:F2}s");

            if (response.PromptEvalCount.HasValue && response.PromptEvalCount > 0)
            {
                var promptTokensPerSec = response.PromptEvalDuration.HasValue && response.PromptEvalDuration > 0
                    ? response.PromptEvalCount.Value / (response.PromptEvalDuration.Value / 1_000_000_000.0)
                    : 0;

                System.Console.WriteLine($"prompt eval count:    {response.PromptEvalCount} token(s)");
                System.Console.WriteLine($"prompt eval duration: {promptEvalDuration.TotalSeconds:F2}s");
                System.Console.WriteLine($"prompt eval rate:     {promptTokensPerSec:F2} tokens/s");
            }

            if (response.EvalCount.HasValue && response.EvalCount > 0)
            {
                var evalTokensPerSec = response.EvalDuration.HasValue && response.EvalDuration > 0
                    ? response.EvalCount.Value / (response.EvalDuration.Value / 1_000_000_000.0)
                    : 0;

                System.Console.WriteLine($"eval count:           {response.EvalCount} token(s)");
                System.Console.WriteLine($"eval duration:        {evalDuration.TotalSeconds:F2}s");
                System.Console.WriteLine($"eval rate:            {evalTokensPerSec:F2} tokens/s");
            }
        }

        #endregion
    }
}
