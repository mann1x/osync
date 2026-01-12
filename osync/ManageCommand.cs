using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Terminal.Gui;
using ByteSizeLib;

namespace osync
{
    // Model information class for TUI display
    public class ManageModelInfo
    {
        // Display fields
        public string Name { get; set; } = "";
        public string ShortId { get; set; } = "";        // 12-char digest
        public long Size { get; set; }
        public string SizeFormatted { get; set; } = "";
        public DateTime ModifiedAt { get; set; }
        public string ModifiedFormatted { get; set; } = "";

        // Extended info (lazy-loaded from /api/show)
        public string FullDigest { get; set; } = "";
        public string Quantization { get; set; } = "";
        public string Family { get; set; } = "";
        public string ParameterSize { get; set; } = "";

        // Modelfile parameters
        public int? NumCtx { get; set; }
        public string? Stop { get; set; }
        public double? Temperature { get; set; }
        public double? TopP { get; set; }
        public int? TopK { get; set; }
        public int? NumBatch { get; set; }
        public int? NumKeep { get; set; }
        public int? RepeatLastN { get; set; }
        public double? FrequencyPenalty { get; set; }

        // State
        public bool IsSelected { get; set; }
        public bool IsLoaded { get; set; }
        public bool ExtendedInfoLoaded { get; set; }
    }

    // Theme definition for color schemes
    public class ManageTheme
    {
        public string Name { get; set; } = "";
        public Terminal.Gui.Attribute ListRowEven { get; set; }
        public Terminal.Gui.Attribute ListRowOdd { get; set; }
        public Terminal.Gui.Attribute SelectedRow { get; set; }
        public Terminal.Gui.Attribute TopBar { get; set; }
        public Terminal.Gui.Attribute BottomBar { get; set; }
    }

    // Sort order enum
    public enum SortOrder
    {
        AlphabeticalAsc,
        AlphabeticalDesc,
        SizeAsc,
        SizeDesc,
        CreatedAsc,
        CreatedDesc
    }

    // Main TUI manager class
    public class ManageUI
    {
        private readonly OsyncProgram _program;
        private readonly string? _destination;
        private List<ManageModelInfo> _allModels = new();
        private List<ManageModelInfo> _filteredModels = new();
        private string _filterText = "";
        private int _currentThemeIndex = 0;
        private SortOrder _currentSortOrder = SortOrder.AlphabeticalAsc;
        private ManageTheme[]? _themes;

        // Dynamic column widths
        private int _sizeColumnWidth = 14;
        private int _paramsColumnWidth = 6;
        private int _quantColumnWidth = 6;
        private int _familyColumnWidth = 10;
        private int _modifiedColumnWidth = 10;
        private int _idColumnWidth = 12;

        // Session state for copy operation
        private string? _lastCopyDestinationServer = null;
        private List<string> _destinationHistory = new();

        // Exit state
        private bool _isNormalExit = false;
        private string? _selectedModelName = null;

        // Terminal.Gui widgets
        private Toplevel? _top;
        private Label? _topBar;
        private FrameView? _modelListFrame;
        private ListView? _modelListView;
        private Label? _bottomBar;

        public ManageUI(OsyncProgram program, string? destination, string? selectedModelName = null)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _destination = destination;
            _selectedModelName = selectedModelName;
        }

        // Initialize 5 hardcoded color themes
        private ManageTheme[] InitializeThemes()
        {
            return new ManageTheme[]
            {
                // Theme 1: Default
                new ManageTheme
                {
                    Name = "Default",
                    ListRowEven = new Terminal.Gui.Attribute(Color.White, Color.Black),
                    ListRowOdd = new Terminal.Gui.Attribute(Color.Gray, Color.Black),
                    SelectedRow = new Terminal.Gui.Attribute(Color.Black, Color.White),
                    TopBar = new Terminal.Gui.Attribute(Color.White, Color.Black),
                    BottomBar = new Terminal.Gui.Attribute(Color.White, Color.Black)
                },

                // Theme 2: Dark Blue
                new ManageTheme
                {
                    Name = "Dark Blue",
                    ListRowEven = new Terminal.Gui.Attribute(Color.Cyan, Color.Blue),
                    ListRowOdd = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue),
                    SelectedRow = new Terminal.Gui.Attribute(Color.Blue, Color.Cyan),
                    TopBar = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue),
                    BottomBar = new Terminal.Gui.Attribute(Color.BrightCyan, Color.Blue)
                },

                // Theme 3: Green Terminal (Retro)
                new ManageTheme
                {
                    Name = "Green Terminal",
                    ListRowEven = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                    ListRowOdd = new Terminal.Gui.Attribute(Color.Green, Color.Black),
                    SelectedRow = new Terminal.Gui.Attribute(Color.Black, Color.BrightGreen),
                    TopBar = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black),
                    BottomBar = new Terminal.Gui.Attribute(Color.BrightGreen, Color.Black)
                },

                // Theme 4: High Contrast
                new ManageTheme
                {
                    Name = "High Contrast",
                    ListRowEven = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
                    ListRowOdd = new Terminal.Gui.Attribute(Color.White, Color.Black),
                    SelectedRow = new Terminal.Gui.Attribute(Color.Black, Color.BrightYellow),
                    TopBar = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black),
                    BottomBar = new Terminal.Gui.Attribute(Color.BrightYellow, Color.Black)
                },

                // Theme 5: Solarized Dark
                new ManageTheme
                {
                    Name = "Solarized Dark",
                    ListRowEven = new Terminal.Gui.Attribute(Color.BrightCyan, Color.DarkGray),
                    ListRowOdd = new Terminal.Gui.Attribute(Color.Cyan, Color.DarkGray),
                    SelectedRow = new Terminal.Gui.Attribute(Color.DarkGray, Color.BrightCyan),
                    TopBar = new Terminal.Gui.Attribute(Color.BrightCyan, Color.DarkGray),
                    BottomBar = new Terminal.Gui.Attribute(Color.BrightCyan, Color.DarkGray)
                }
            };
        }

        // Main entry point - run the TUI
        public void Run()
        {
            try
            {
                // Detect Linux/SSH environment and configure Terminal.Gui accordingly
                bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
                bool isSsh = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CLIENT")) ||
                             !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_TTY"));

                // For Linux/SSH, force use of the CursesDriver which has better SSH compatibility
                // and explicitly set 16-color ANSI mode instead of auto-detection
                if (isLinux || isSsh)
                {
                    // Set TERM to xterm-256color if not already set, to ensure proper color support
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM")))
                    {
                        Environment.SetEnvironmentVariable("TERM", "xterm-256color");
                    }

                    // Force use of UseSystemConsole which works better in SSH
                    Application.UseSystemConsole = true;
                }

                Application.Init();
            }
            catch
            {
                // If Terminal.Gui fails to initialize (e.g., after console resize or state issues),
                // this is likely because Application.Shutdown() didn't fully clean up.
                // Try shutdown and a more aggressive cleanup.
                try
                {
                    Application.Shutdown();
                }
                catch
                {
                    // Ignore shutdown errors
                }

                // Force garbage collection to clean up Terminal.Gui resources
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                System.Threading.Thread.Sleep(100);

                try
                {
                    // Reapply Linux/SSH configuration for retry
                    bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
                    bool isSsh = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CLIENT")) ||
                                 !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_TTY"));

                    if (isLinux || isSsh)
                    {
                        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TERM")))
                        {
                            Environment.SetEnvironmentVariable("TERM", "xterm-256color");
                        }
                        Application.UseSystemConsole = true;
                    }

                    Application.Init();
                }
                catch
                {
                    Console.WriteLine($"\nTerminal.Gui failed to reinitialize after the previous operation.");
                    Console.WriteLine($"This is a known limitation when returning to manage view.");
                    Console.WriteLine($"\nTo use manage view again, please restart osync with: osync manage");
                    return;
                }
            }

            try
            {
                // Reset widget references (they're invalid after previous shutdown)
                _top = null;
                _topBar = null;
                _modelListFrame = null;
                _modelListView = null;
                _bottomBar = null;

                // Initialize themes after Application.Init()
                _themes = InitializeThemes();
                SetupUI();
                LoadModels();

                // Subscribe to window resize events
                Application.Top.Resized += OnWindowResized;

                Application.Run(_top);
            }
            catch (Exception ex)
            {
                // Only show error if this wasn't a normal exit (Ctrl+Q)
                // Silently ignore NullReferenceException during shutdown (Terminal.Gui cleanup issue)
                if (!_isNormalExit && !(ex is NullReferenceException))
                {
                    Console.WriteLine($"\nError in manage view: {ex.Message}");
                    Console.WriteLine($"\nTo use manage view again, please restart osync with: osync manage");
                }
            }
            finally
            {
                try
                {
                    Application.Shutdown();
                    // Reset widget references after shutdown
                    _top = null;
                    _topBar = null;
                    _modelListFrame = null;
                    _modelListView = null;
                    _bottomBar = null;
                }
                catch
                {
                    // Ignore shutdown errors
                }
            }
        }

        // Setup the borderless UI layout
        private void SetupUI()
        {
            if (_themes == null) throw new InvalidOperationException("Themes not initialized");

            _top = Application.Top;

            // Top bar (no border) - filter on left, version on right
            _topBar = new Label("Loading...")
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                ColorScheme = new ColorScheme { Normal = _themes[_currentThemeIndex].TopBar }
            };
            _top.Add(_topBar);

            // Model list frame (no border, fills middle area)
            _modelListFrame = new FrameView("")
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),  // Leave room for bottom bar
                Border = new Border { BorderStyle = BorderStyle.None }
            };

            // List view for models
            _modelListView = new ListView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                AllowsMarking = false,
                CanFocus = true,
                ColorScheme = new ColorScheme
                {
                    Normal = _themes[_currentThemeIndex].ListRowEven,
                    Focus = _themes[_currentThemeIndex].SelectedRow,
                    HotNormal = _themes[_currentThemeIndex].ListRowOdd,
                    HotFocus = _themes[_currentThemeIndex].SelectedRow
                }
            };

            _modelListView.KeyPress += OnKeyPress;
            _modelListFrame.Add(_modelListView);
            _top.Add(_modelListFrame);

            // Bottom bar (no border) - compact action shortcuts
            _bottomBar = new Label("Ctrl+|c=cp|m=ren|r=run|s=sh|d=del|u=upd|p=pull|l=load|k=unl|x=ps|o=sort|t=theme|q=quit")
            {
                X = 0,
                Y = Pos.Bottom(_top) - 1,
                Width = Dim.Fill(),
                Height = 1,
                ColorScheme = new ColorScheme { Normal = _themes[_currentThemeIndex].BottomBar }
            };
            _top.Add(_bottomBar);

            UpdateTopBar();
        }

        // Helper method to enable clipboard paste (Ctrl+V) and right-click paste on text fields
        private void EnableClipboardPaste(TextField textField)
        {
            // Handle Ctrl+V for paste
            textField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == (Key.V | Key.CtrlMask))
                {
                    try
                    {
                        // Get clipboard contents
                        var clipboardText = Terminal.Gui.Clipboard.Contents?.ToString();
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            // Insert at cursor position
                            var currentText = textField.Text?.ToString() ?? "";
                            var cursorPos = textField.CursorPosition;

                            // Insert clipboard text at cursor position
                            var newText = currentText.Insert(cursorPos, clipboardText);
                            textField.Text = newText;

                            // Move cursor to end of pasted text
                            textField.CursorPosition = cursorPos + clipboardText.Length;

                            e.Handled = true;
                        }
                    }
                    catch
                    {
                        // Silently ignore clipboard errors
                    }
                }
            };

            // Handle right mouse click for paste
            textField.MouseClick += (e) =>
            {
                if (e.MouseEvent.Flags.HasFlag(MouseFlags.Button3Clicked))
                {
                    try
                    {
                        // Get clipboard contents
                        var clipboardText = Terminal.Gui.Clipboard.Contents?.ToString();
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            // Replace entire text with clipboard content
                            textField.Text = clipboardText;
                            textField.CursorPosition = clipboardText.Length;
                            e.Handled = true;
                        }
                    }
                    catch
                    {
                        // Silently ignore clipboard errors
                    }
                }
            };
        }

        // Handle window resize events
        private void OnWindowResized(Size obj)
        {
            try
            {
                // Recalculate column widths based on new window size
                CalculateColumnWidths();

                // Refresh the model list to adapt to new width
                if (_modelListView != null)
                {
                    UpdateModelList();
                }

                // Refresh top bar to update layout
                UpdateTopBar();
            }
            catch
            {
                // Silently ignore resize errors during shutdown
            }
        }

        // Load models from local or remote server
        private void LoadModels()
        {
            try
            {
                if (string.IsNullOrEmpty(_destination))
                {
                    FetchLocalModels();
                }
                else
                {
                    FetchRemoteModels();
                }

                // Fetch extended info for all models to get family and parameter size
                foreach (var model in _allModels)
                {
                    FetchExtendedInfo(model);
                }

                // Fetch running status to mark loaded models
                FetchRunningStatus();

                // Calculate dynamic column widths based on actual content
                CalculateColumnWidths();

                _filteredModels = _allModels;
                ApplySortOrder();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to load models: {ex.Message}", "OK");
            }
        }

        // Calculate optimal column widths based on actual data
        private void CalculateColumnWidths()
        {
            if (_allModels.Count == 0) return;

            // Get current terminal width
            var termWidth = Application.Driver?.Cols ?? 80;

            // Calculate maximum width needed for each column based on content
            var contentSizeWidth = _allModels.Max(m => m.SizeFormatted.Length);
            var contentParamsWidth = _allModels.Max(m => string.IsNullOrEmpty(m.ParameterSize) ? 0 : m.ParameterSize.Length);
            var contentQuantWidth = _allModels.Max(m => string.IsNullOrEmpty(m.Quantization) ? 7 : m.Quantization.Length);
            var contentFamilyWidth = _allModels.Max(m => string.IsNullOrEmpty(m.Family) ? 0 : m.Family.Length);
            var contentModifiedWidth = _allModels.Max(m => m.ModifiedFormatted.Length);

            // Ensure minimum widths
            _sizeColumnWidth = Math.Max(contentSizeWidth, 6);
            _paramsColumnWidth = Math.Max(contentParamsWidth, 4); // Minimum width for params like "70B"
            _quantColumnWidth = Math.Max(contentQuantWidth, 6);
            _familyColumnWidth = Math.Max(contentFamilyWidth, 6);
            _modifiedColumnWidth = Math.Max(contentModifiedWidth, 3); // Minimum width for "now", "1y", etc.
            _idColumnWidth = 12; // ID is always 12 characters from digest (same as ollama ls)

            // Calculate total fixed width (checkbox + columns + spaces between)
            // Format: "[x] name size params quant family modified id"
            var minNameWidth = 20;
            var fixedWidth = 4 + _sizeColumnWidth + _paramsColumnWidth + _quantColumnWidth +
                            _familyColumnWidth + _modifiedColumnWidth + _idColumnWidth + 6 + minNameWidth;

            // If the terminal is too narrow, reduce column widths proportionally
            if (termWidth < fixedWidth)
            {
                // Calculate scale factor to fit within terminal width
                var availableWidth = termWidth - (4 + minNameWidth + 6); // Reserve space for checkbox, name, and spaces
                var currentTotalWidth = _sizeColumnWidth + _paramsColumnWidth + _quantColumnWidth +
                                       _familyColumnWidth + _modifiedColumnWidth + _idColumnWidth;

                if (currentTotalWidth > availableWidth && availableWidth > 20)
                {
                    var scaleFactor = (double)availableWidth / currentTotalWidth;

                    // Scale down columns proportionally, but maintain minimums
                    _sizeColumnWidth = Math.Max(4, (int)(_sizeColumnWidth * scaleFactor));
                    _paramsColumnWidth = Math.Max(3, (int)(_paramsColumnWidth * scaleFactor));
                    _quantColumnWidth = Math.Max(4, (int)(_quantColumnWidth * scaleFactor));
                    _familyColumnWidth = Math.Max(4, (int)(_familyColumnWidth * scaleFactor));
                    _modifiedColumnWidth = Math.Max(2, (int)(_modifiedColumnWidth * scaleFactor));
                    _idColumnWidth = Math.Max(6, (int)(_idColumnWidth * scaleFactor));
                }
            }
        }

        // Fetch models from local filesystem
        private void FetchLocalModels()
        {
            _allModels = new List<ManageModelInfo>();
            string manifestsDir = Path.Combine(_program.ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                return;
            }

            // Scan all hosts (registry.ollama.ai, hf.co, hub, etc.)
            foreach (string hostDir in Directory.GetDirectories(manifestsDir))
            {
                string host = Path.GetFileName(hostDir);

                // Scan all namespaces within each host
                foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                {
                    string ns = Path.GetFileName(namespaceDir);

                    // Scan all models within each namespace
                    foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                    {
                        string model = Path.GetFileName(modelDir);

                        // Tags are files directly in the model directory
                        foreach (string tagFile in Directory.GetFiles(modelDir))
                        {
                            string tag = Path.GetFileName(tagFile);

                            // Build display name based on host/namespace
                            string fullModelName;
                            if (host == "registry.ollama.ai" && ns == "library")
                            {
                                fullModelName = $"{model}:{tag}";
                            }
                            else if (host == "registry.ollama.ai")
                            {
                                fullModelName = $"{ns}/{model}:{tag}";
                            }
                            else
                            {
                                fullModelName = $"{host}/{ns}/{model}:{tag}";
                            }

                            var fileInfo = new FileInfo(tagFile);
                            long totalSize = 0;
                            string modelId = "";
                            string quantization = "";
                            string family = "";

                            try
                            {
                                var manifest = ManifestReader.Read<RootManifest>(tagFile);
                                if (manifest?.layers != null)
                                {
                                    totalSize = manifest.layers.Sum(l => l.size);
                                }

                                // Compute SHA256 of manifest file content (same as ollama ls)
                                var manifestBytes = System.IO.File.ReadAllBytes(tagFile);
                                using var sha256 = System.Security.Cryptography.SHA256.Create();
                                var hashBytes = sha256.ComputeHash(manifestBytes);
                                var fullDigest = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                                modelId = fullDigest.Substring(0, 12);
                            }
                            catch { }

                            var sizeBytes = ByteSize.FromBytes(totalSize);
                            string sizeFormatted = sizeBytes.GigaBytes >= 1
                                ? $"{sizeBytes.GigaBytes:F1}GB"
                                : $"{sizeBytes.MegaBytes:F0}MB";

                            _allModels.Add(new ManageModelInfo
                            {
                                Name = fullModelName,
                                ShortId = modelId,
                                FullDigest = modelId,
                                Size = totalSize,
                                SizeFormatted = sizeFormatted,
                                ModifiedAt = fileInfo.LastWriteTime,
                                ModifiedFormatted = GetTimeAgo(fileInfo.LastWriteTime),
                                Quantization = quantization,
                                Family = family,
                                IsSelected = false,
                                IsLoaded = false,
                                ExtendedInfoLoaded = false
                            });
                        }
                    }
                }
            }

            // Sort by name by default
            _allModels = _allModels.OrderBy(m => m.Name).ToList();
        }

        // Fetch models from remote server
        private void FetchRemoteModels()
        {
            _allModels = new List<ManageModelInfo>();

            if (string.IsNullOrEmpty(_destination))
            {
                throw new Exception("Destination server not specified");
            }

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(_destination),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                var response = httpClient.GetAsync("api/tags").Result;
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }

                string json = response.Content.ReadAsStringAsync().Result;
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

                if (modelsResponse?.models != null)
                {
                    foreach (var model in modelsResponse.models)
                    {
                        string modelId = "";
                        if (model.digest?.StartsWith("sha256:") == true)
                        {
                            modelId = model.digest.Substring(7, 12);
                        }
                        else if (!string.IsNullOrEmpty(model.digest))
                        {
                            modelId = model.digest.Substring(0, Math.Min(12, model.digest.Length));
                        }

                        var sizeBytes = ByteSize.FromBytes(model.size);
                        string sizeFormatted = sizeBytes.GigaBytes >= 1
                            ? $"{sizeBytes.GigaBytes:F1}GB"
                            : $"{sizeBytes.MegaBytes:F0}MB";

                        _allModels.Add(new ManageModelInfo
                        {
                            Name = model.name ?? "",
                            ShortId = modelId,
                            FullDigest = model.digest ?? "",
                            Size = model.size,
                            SizeFormatted = sizeFormatted,
                            ModifiedAt = model.modified_at,
                            ModifiedFormatted = GetTimeAgo(model.modified_at),
                            Quantization = "",
                            Family = "",
                            IsSelected = false,
                            IsLoaded = false,
                            ExtendedInfoLoaded = false
                        });
                    }
                }

                // Sort by name by default
                _allModels = _allModels.OrderBy(m => m.Name).ToList();
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch remote models: {ex.Message}", ex);
            }
        }

        // Format time ago string (compact for TUI)
        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes}m";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours}h";
            if (timeSpan.TotalDays < 7)
                return $"{(int)timeSpan.TotalDays}d";
            if (timeSpan.TotalDays < 30)
                return $"{(int)(timeSpan.TotalDays / 7)}w";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)}mo";
            return $"{(int)(timeSpan.TotalDays / 365)}y";
        }

        // Fetch extended info for a model (lazy-loaded when user presses right arrow)
        private void FetchExtendedInfo(ManageModelInfo model)
        {
            if (model.ExtendedInfoLoaded)
                return;

            try
            {
                string serverUrl = string.IsNullOrEmpty(_destination)
                    ? "http://localhost:11434"
                    : _destination;

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var showRequest = new
                {
                    name = model.Name,
                    verbose = false
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(showRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var response = httpClient.PostAsync("api/show", content).Result;
                if (response.IsSuccessStatusCode)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Extract details from the response
                    if (root.TryGetProperty("details", out var details))
                    {
                        if (details.TryGetProperty("family", out var family))
                        {
                            model.Family = family.GetString() ?? "";
                        }
                        if (details.TryGetProperty("quantization_level", out var quant))
                        {
                            model.Quantization = quant.GetString() ?? "";
                        }
                        if (details.TryGetProperty("parameter_size", out var paramSize))
                        {
                            // Store parameter size separately (e.g., "7B", "13B")
                            model.ParameterSize = paramSize.GetString() ?? "";
                        }
                    }

                    // Extract parameters
                    if (root.TryGetProperty("parameters", out var paramsElement))
                    {
                        var paramsStr = paramsElement.GetString();
                        if (!string.IsNullOrEmpty(paramsStr))
                        {
                            // Parse parameters from string format
                            var lines = paramsStr.Split('\n');
                            foreach (var line in lines)
                            {
                                var parts = line.Trim().Split(new[] { ' ' }, 2);
                                if (parts.Length == 2)
                                {
                                    var key = parts[0].ToLowerInvariant();
                                    var value = parts[1];

                                    switch (key)
                                    {
                                        case "num_ctx":
                                            if (int.TryParse(value, out int ctx))
                                                model.NumCtx = ctx;
                                            break;
                                        case "stop":
                                            model.Stop = value;
                                            break;
                                        case "temperature":
                                            if (double.TryParse(value, out double temp))
                                                model.Temperature = temp;
                                            break;
                                        case "top_p":
                                            if (double.TryParse(value, out double topP))
                                                model.TopP = topP;
                                            break;
                                        case "top_k":
                                            if (int.TryParse(value, out int topK))
                                                model.TopK = topK;
                                            break;
                                    }
                                }
                            }
                        }
                    }

                    model.ExtendedInfoLoaded = true;
                }
            }
            catch
            {
                // Silently fail - extended info is optional
            }
        }

        // Fetch running status from /api/ps to mark loaded models
        private void FetchRunningStatus()
        {
            try
            {
                string serverUrl = string.IsNullOrEmpty(_destination)
                    ? "http://localhost:11434"
                    : _destination;

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = httpClient.GetAsync("/api/ps").Result;
                if (response.IsSuccessStatusCode)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                    if (status?.Models != null)
                    {
                        var loadedModelNames = status.Models.Select(m => m.Name).ToHashSet();

                        // Mark models as loaded
                        foreach (var model in _allModels)
                        {
                            model.IsLoaded = loadedModelNames.Contains(model.Name);
                        }

                        // Update filtered models too
                        foreach (var model in _filteredModels)
                        {
                            model.IsLoaded = loadedModelNames.Contains(model.Name);
                        }
                    }
                }
            }
            catch
            {
                // Silently fail - running status is optional
            }
        }

        // Update the model list view
        private void UpdateModelList()
        {
            if (_modelListView == null) return;

            var items = new List<string>();
            for (int i = 0; i < _filteredModels.Count; i++)
            {
                var model = _filteredModels[i];
                var checkbox = model.IsSelected ? "[X]" : "[ ]";
                var line = FormatModelLine(checkbox, model, i % 2 == 0);
                items.Add(line);
            }

            _modelListView.SetSource(items);

            // Restore selected model if specified
            if (!string.IsNullOrEmpty(_selectedModelName))
            {
                for (int i = 0; i < _filteredModels.Count; i++)
                {
                    if (_filteredModels[i].Name == _selectedModelName)
                    {
                        _modelListView.SelectedItem = i;
                        _selectedModelName = null; // Clear after restoring once
                        break;
                    }
                }
            }

            _modelListView.SetNeedsDisplay();
        }

        // Format a single model line
        private string FormatModelLine(string checkbox, ManageModelInfo model, bool isEvenRow)
        {
            // Calculate available width for model name
            var termWidth = Application.Driver?.Cols ?? 80;

            // Calculate fixed width using dynamic column widths
            var fixedWidth = 4 + _sizeColumnWidth + _paramsColumnWidth + _quantColumnWidth + _familyColumnWidth + _modifiedColumnWidth + _idColumnWidth + 6; // checkbox + columns + spaces
            var nameWidth = Math.Max(20, termWidth - fixedWidth);

            var name = model.Name.Length > nameWidth
                ? model.Name.Substring(0, nameWidth - 3) + "..."
                : model.Name.PadRight(nameWidth);

            // Right-align all data columns with dynamic widths - no truncation needed
            var size = model.SizeFormatted.PadLeft(_sizeColumnWidth);
            var params_ = (string.IsNullOrEmpty(model.ParameterSize) ? "" : model.ParameterSize).PadLeft(_paramsColumnWidth);
            var quant = (string.IsNullOrEmpty(model.Quantization) ? "unknown" : model.Quantization).PadLeft(_quantColumnWidth);
            var family = (string.IsNullOrEmpty(model.Family) ? "" : model.Family).PadLeft(_familyColumnWidth);
            var modified = model.ModifiedFormatted.PadLeft(_modifiedColumnWidth);

            // Format ID: take first 12 chars from digest (same as ollama ls), right-align
            var shortId = string.IsNullOrEmpty(model.ShortId) ? "".PadLeft(_idColumnWidth) :
                          (model.ShortId.Length >= 12 ? model.ShortId.Substring(0, 12) : model.ShortId).PadLeft(_idColumnWidth);

            // Note: Terminal.Gui ListView doesn't support per-row coloring natively
            // The isEvenRow parameter is kept for potential future enhancements
            return $"{checkbox} {name} {size} {params_} {quant} {family} {modified} {shortId}";
        }

        // Update top bar with filter and version info
        private void UpdateTopBar()
        {
            if (_topBar == null) return;

            // Map sort order to display string
            var sortDisplay = _currentSortOrder switch
            {
                SortOrder.AlphabeticalAsc => "Name+",
                SortOrder.AlphabeticalDesc => "Name-",
                SortOrder.SizeDesc => "Size-",
                SortOrder.SizeAsc => "Size+",
                SortOrder.CreatedDesc => "Created-",
                SortOrder.CreatedAsc => "Created+",
                _ => "Name+"
            };

            // Build left side: Sorting and optionally Filter
            var leftSide = $"Sorting: {sortDisplay}";
            if (!string.IsNullOrEmpty(_filterText))
            {
                leftSide += $" Filter: {_filterText}";
            }

            var version = "osync manage v1.1.6";
            var termWidth = Application.Driver?.Cols ?? 80;
            var spacing = Math.Max(1, termWidth - leftSide.Length - version.Length);

            _topBar.Text = leftSide + new string(' ', spacing) + version;
        }

        // Keyboard event handler
        private void OnKeyPress(View.KeyEventEventArgs args)
        {
            var key = args.KeyEvent.Key;

            // Navigation keys
            if (key == Key.CursorUp || key == Key.CursorDown ||
                key == Key.PageUp || key == Key.PageDown ||
                key == Key.Home || key == Key.End)
            {
                // Let ListView handle these
                return;
            }

            // Space - toggle selection
            if (key == (Key)' ')
            {
                ToggleSelection();
                args.Handled = true;
                return;
            }

            // Escape - close dialogs, clear filter, or exit with confirmation
            if (key == Key.Esc)
            {
                if (!string.IsNullOrEmpty(_filterText))
                {
                    _filterText = "";
                    FilterModels();
                    UpdateTopBar();
                }
                else
                {
                    // Ask for confirmation before exiting
                    var result = MessageBox.Query("Exit Manage",
                        "Are you sure you want to exit manage mode?",
                        "No", "Yes");
                    if (result == 1)  // Yes was pressed
                    {
                        Application.RequestStop();
                    }
                }
                args.Handled = true;
                return;
            }

            // Backspace - remove last filter character
            if (key == Key.Backspace)
            {
                if (_filterText.Length > 0)
                {
                    _filterText = _filterText.Substring(0, _filterText.Length - 1);
                    FilterModels();
                    UpdateTopBar();
                }
                args.Handled = true;
                return;
            }

            // Right arrow - show extended model info
            if (key == Key.CursorRight)
            {
                ShowExtendedInfo();
                args.Handled = true;
                return;
            }

            // Ctrl+Q or Cmd+Q - quit
            if ((key & Key.CtrlMask) == Key.CtrlMask && (key & ~Key.CtrlMask) == (Key)'Q')
            {
                _isNormalExit = true;
                Application.RequestStop();
                args.Handled = true;
                return;
            }

            // Action shortcuts (Ctrl+key)
            if ((key & Key.CtrlMask) == Key.CtrlMask)
            {
                var actionKey = key & ~Key.CtrlMask;
                HandleActionShortcut(actionKey);
                args.Handled = true;
                return;
            }

            // Alphanumeric - add to filter
            if (char.IsLetterOrDigit((char)key) || key == (Key)':' || key == (Key)'-' || key == (Key)'_')
            {
                _filterText += (char)key;
                FilterModels();
                UpdateTopBar();
                args.Handled = true;
            }
        }

        // Toggle selection on current model
        private void ToggleSelection()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index >= 0 && index < _filteredModels.Count)
            {
                _filteredModels[index].IsSelected = !_filteredModels[index].IsSelected;
                UpdateModelList();
                // Restore cursor position after update
                _modelListView.SelectedItem = index;
            }
        }

        // Filter models based on filter text
        private void FilterModels()
        {
            if (string.IsNullOrEmpty(_filterText))
            {
                _filteredModels = _allModels;
            }
            else
            {
                var pattern = _filterText.Replace("*", ".*");
                _filteredModels = _allModels.Where(m =>
                    System.Text.RegularExpressions.Regex.IsMatch(m.Name, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ).ToList();
            }
            ApplySortOrder();
        }

        // Handle action shortcuts (Ctrl+key)
        private void HandleActionShortcut(Key key)
        {
            switch ((char)key)
            {
                case 'C':
                case 'c':
                    ExecuteCopy();
                    break;
                case 'M':
                case 'm':
                    ExecuteRename();
                    break;
                case 'R':
                case 'r':
                    ExecuteRun();
                    break;
                case 'S':
                case 's':
                    ExecuteShow();
                    break;
                case 'D':
                case 'd':
                    ExecuteDelete();
                    break;
                case 'U':
                case 'u':
                    ExecuteUpdate();
                    break;
                case 'P':
                case 'p':
                    ExecutePull();
                    break;
                case 'L':
                case 'l':
                    ExecuteLoad();
                    break;
                case 'K':
                case 'k':
                    ExecuteUnload();
                    break;
                case 'X':
                case 'x':
                    ExecutePs();
                    break;
                case 'O':
                case 'o':
                    CycleSortOrder();
                    break;
                case 'T':
                case 't':
                    CycleTheme();
                    break;
            }
        }

        // Cycle through color themes
        private void CycleTheme()
        {
            if (_themes == null) return;
            _currentThemeIndex = (_currentThemeIndex + 1) % _themes.Length;
            ApplyTheme();
        }

        // Apply current theme to UI elements
        private void ApplyTheme()
        {
            if (_themes == null) return;
            var theme = _themes[_currentThemeIndex];
            if (_topBar != null)
                _topBar.ColorScheme = new ColorScheme { Normal = theme.TopBar };
            if (_bottomBar != null)
                _bottomBar.ColorScheme = new ColorScheme { Normal = theme.BottomBar };
            if (_modelListView != null)
                _modelListView.ColorScheme = new ColorScheme
                {
                    Normal = theme.ListRowEven,
                    Focus = theme.SelectedRow,
                    HotNormal = theme.ListRowOdd,
                    HotFocus = theme.SelectedRow
                };

            // Redraw
            Application.Refresh();
        }

        // Cycle through sort orders
        private void CycleSortOrder()
        {
            if (_modelListView == null) return;

            // Save current model name to restore cursor position
            string? currentModelName = null;
            var currentIndex = _modelListView.SelectedItem;
            if (currentIndex >= 0 && currentIndex < _filteredModels.Count)
            {
                currentModelName = _filteredModels[currentIndex].Name;
            }

            // Cycle to next sort order
            _currentSortOrder = _currentSortOrder switch
            {
                SortOrder.AlphabeticalAsc => SortOrder.AlphabeticalDesc,
                SortOrder.AlphabeticalDesc => SortOrder.SizeDesc,
                SortOrder.SizeDesc => SortOrder.SizeAsc,
                SortOrder.SizeAsc => SortOrder.CreatedDesc,
                SortOrder.CreatedDesc => SortOrder.CreatedAsc,
                SortOrder.CreatedAsc => SortOrder.AlphabeticalAsc,
                _ => SortOrder.AlphabeticalAsc
            };

            ApplySortOrder();
            UpdateTopBar();

            // Restore cursor to same model if found
            if (currentModelName != null)
            {
                var newIndex = _filteredModels.FindIndex(m => m.Name == currentModelName);
                if (newIndex >= 0)
                {
                    _modelListView.SelectedItem = newIndex;

                    // Ensure the selected item is visible by scrolling if necessary
                    var visibleHeight = _modelListView.Bounds.Height - 2; // Account for borders
                    var currentTopItem = _modelListView.TopItem;

                    // If selected item is above the visible area
                    if (newIndex < currentTopItem)
                    {
                        _modelListView.TopItem = newIndex;
                    }
                    // If selected item is below the visible area
                    else if (newIndex >= currentTopItem + visibleHeight)
                    {
                        _modelListView.TopItem = Math.Max(0, newIndex - visibleHeight + 1);
                    }
                }
            }
        }

        // Apply current sort order to filtered models
        private void ApplySortOrder()
        {
            switch (_currentSortOrder)
            {
                case SortOrder.AlphabeticalAsc:
                    _filteredModels = _filteredModels.OrderBy(m => m.Name).ToList();
                    break;
                case SortOrder.AlphabeticalDesc:
                    _filteredModels = _filteredModels.OrderByDescending(m => m.Name).ToList();
                    break;
                case SortOrder.SizeAsc:
                    _filteredModels = _filteredModels.OrderBy(m => m.Size).ToList();
                    break;
                case SortOrder.SizeDesc:
                    _filteredModels = _filteredModels.OrderByDescending(m => m.Size).ToList();
                    break;
                case SortOrder.CreatedAsc:
                    _filteredModels = _filteredModels.OrderBy(m => m.ModifiedAt).ToList();
                    break;
                case SortOrder.CreatedDesc:
                    _filteredModels = _filteredModels.OrderByDescending(m => m.ModifiedAt).ToList();
                    break;
            }

            UpdateModelList();
        }

        // Action: Copy model(s)
        private void ExecuteCopy()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            // Get selected models (checked) or current model if none checked
            var selectedModels = _filteredModels.Where(m => m.IsSelected).ToList();
            bool isBatchCopy = selectedModels.Count > 0;

            if (!isBatchCopy)
            {
                var index = _modelListView.SelectedItem;
                if (index < 0 || index >= _filteredModels.Count) return;
                selectedModels = new List<ManageModelInfo> { _filteredModels[index] };
            }

            // Create input dialog for destination
            string title = isBatchCopy ? $"Copy {selectedModels.Count} models" : $"Copy {selectedModels[0].Name}";
            var dialog = new Dialog(title, 60, 18);

            TextField? destNameField = null;

            // For single copy, show destination name field
            if (!isBatchCopy)
            {
                var nameLabel = new Label("Enter destination name:")
                {
                    X = 1,
                    Y = 1
                };
                dialog.Add(nameLabel);

                destNameField = new TextField(selectedModels[0].Name)
                {
                    X = 1,
                    Y = 2,
                    Width = Dim.Fill() - 2
                };
                EnableClipboardPaste(destNameField);
                dialog.Add(destNameField);
            }

            var serverLabel = new Label(isBatchCopy ? "Remote server (required):" : "Remote server (empty for local):")
            {
                X = 1,
                Y = isBatchCopy ? 1 : 4
            };
            dialog.Add(serverLabel);

            var serverField = new TextField(_lastCopyDestinationServer ?? "")
            {
                X = 1,
                Y = isBatchCopy ? 2 : 5,
                Width = Dim.Fill() - 2
            };
            EnableClipboardPaste(serverField);
            dialog.Add(serverField);

            var throttleLabel = new Label("Bandwidth throttle (empty=unlimited, e.g., 10MB):")
            {
                X = 1,
                Y = isBatchCopy ? 4 : 7
            };
            dialog.Add(throttleLabel);

            var throttleField = new TextField("")
            {
                X = 1,
                Y = isBatchCopy ? 5 : 8,
                Width = Dim.Fill() - 2
            };
            EnableClipboardPaste(throttleField);
            dialog.Add(throttleField);

            // Track current history index for Tab cycling
            int historyIndex = -1;

            // Handle Tab key for cycling through history (only if field is empty and has history)
            serverField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Tab &&
                    string.IsNullOrEmpty(serverField.Text.ToString()) &&
                    _destinationHistory.Count > 0)
                {
                    e.Handled = true;
                    historyIndex = (historyIndex + 1) % _destinationHistory.Count;
                    serverField.Text = _destinationHistory[historyIndex];
                }
            };

            var copyButton = new Button("Copy")
            {
                X = Pos.Center() - 10,
                Y = isBatchCopy ? 7 : 11
            };
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = isBatchCopy ? 7 : 11
            };

            Action performCopy = () =>
            {
                var destName = destNameField?.Text.ToString()?.Trim();
                var destServer = serverField.Text.ToString()?.Trim();
                var throttle = throttleField.Text.ToString()?.Trim();

                // Validation
                if (!isBatchCopy && string.IsNullOrWhiteSpace(destName))
                {
                    MessageBox.ErrorQuery("Error", "Destination name cannot be empty", "OK");
                    return;
                }

                if (isBatchCopy && string.IsNullOrWhiteSpace(destServer))
                {
                    MessageBox.ErrorQuery("Error", "Remote server is required for batch copy", "OK");
                    return;
                }

                // Check if this is a local copy (no remote server specified)
                // Important: if manage itself is connected to a remote server, "local" means on that remote server
                bool isLocalCopy = string.IsNullOrEmpty(destServer);

                // For single local copy, check if destination already exists
                if (!isBatchCopy && isLocalCopy)
                {
                    if (_allModels.Any(m => m.Name.Equals(destName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.ErrorQuery("Error", $"Model '{destName}' already exists", "OK");
                        return;
                    }
                }

                Application.RequestStop();

                // Save destination server to history
                if (!isLocalCopy && !string.IsNullOrEmpty(destServer))
                {
                    _lastCopyDestinationServer = destServer;
                    if (!_destinationHistory.Contains(destServer))
                    {
                        _destinationHistory.Insert(0, destServer);
                        if (_destinationHistory.Count > 10) // Keep only last 10
                        {
                            _destinationHistory.RemoveAt(_destinationHistory.Count - 1);
                        }
                    }
                }

                // Exit TUI to show console output
                Application.Shutdown();

                // Set bandwidth throttling if specified
                if (!string.IsNullOrEmpty(throttle))
                {
                    _program.BandwidthThrottling = throttle;
                }

                // Enable exception throwing instead of Environment.Exit for manage mode
                _program.ThrowOnError = true;

                // Create cancellation token source for Ctrl+C handling
                var cts = new CancellationTokenSource();
                ConsoleCancelEventHandler? cancelHandler = null;

                // Setup Ctrl+C and Ctrl+Break handler
                cancelHandler = (sender, e) =>
                {
                    e.Cancel = true; // Prevent the process from terminating
                    Console.WriteLine("\n\nOperation cancelled. Returning to manage view...");
                    cts.Cancel(); // Signal cancellation

                    // Remove the handler to prevent multiple invocations
                    if (cancelHandler != null)
                    {
                        Console.CancelKeyPress -= cancelHandler;
                    }
                };

                Console.CancelKeyPress += cancelHandler;

                try
                {
                    if (isBatchCopy)
                    {
                        // Batch copy: copy all selected models to remote server
                        Console.WriteLine($"Copying {selectedModels.Count} models to {destServer}...\n");

                        int successCount = 0;
                        int failCount = 0;

                        foreach (var mdl in selectedModels)
                        {
                            try
                            {
                                Console.WriteLine($"--- Copying {mdl.Name} ({successCount + failCount + 1}/{selectedModels.Count}) ---");

                                string source;
                                if (string.IsNullOrEmpty(_destination))
                                {
                                    source = mdl.Name;
                                }
                                else
                                {
                                    source = _destination.TrimEnd('/') + "/" + mdl.Name;
                                }

                                string destination = destServer!.TrimEnd('/') + "/" + mdl.Name;

                                _program.ActionCopy(source, destination, null, cts.Token);
                                successCount++;
                                Console.WriteLine($"✓ Successfully copied {mdl.Name}\n");
                            }
                            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
                            {
                                Console.WriteLine($"\nBatch copy cancelled: {successCount} succeeded, {failCount} failed");
                                throw;
                            }
                            catch (Exception ex)
                            {
                                // Check if cancellation is in inner exception
                                if (ex.InnerException is OperationCanceledException || ex.InnerException is TaskCanceledException)
                                {
                                    Console.WriteLine($"\nBatch copy cancelled: {successCount} succeeded, {failCount} failed");
                                    throw new OperationCanceledException();
                                }
                                failCount++;
                                Console.WriteLine($"✗ Failed to copy {mdl.Name}: {ex.Message}\n");
                            }
                        }

                        Console.WriteLine($"\nBatch copy completed: {successCount} succeeded, {failCount} failed");
                    }
                    else
                    {
                        // Single copy
                        string source;
                        string destination;

                        if (string.IsNullOrEmpty(_destination))
                        {
                            source = selectedModels[0].Name;
                        }
                        else
                        {
                            source = _destination.TrimEnd('/') + "/" + selectedModels[0].Name;
                        }

                        if (isLocalCopy)
                        {
                            // If manage is connected to a remote server, "local copy" means copy on that remote server
                            if (!string.IsNullOrEmpty(_destination))
                            {
                                destination = _destination.TrimEnd('/') + "/" + destName;
                            }
                            else
                            {
                                destination = destName!;
                            }
                        }
                        else
                        {
                            destination = destServer!.TrimEnd('/') + "/" + destName;
                        }

                        _program.ActionCopy(source, destination, null, cts.Token);
                    }

                    // Remove cancel handler
                    if (cancelHandler != null)
                    {
                        Console.CancelKeyPress -= cancelHandler;
                    }

                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    Console.WriteLine("Reinitializing manage view...");

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    // Remember the first model from the selection
                    var newUI = new ManageUI(_program, _destination, selectedModels[0].Name);
                    newUI.Run();

                    // When the new ManageUI exits, terminate the entire process to prevent
                    // returning to the old shutdown TUI. The new ManageUI has taken over.
                    Environment.Exit(0);
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    // Remove cancel handler
                    if (cancelHandler != null)
                    {
                        Console.CancelKeyPress -= cancelHandler;
                    }

                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    Console.WriteLine("Reinitializing manage view...");

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    var newUI = new ManageUI(_program, _destination, selectedModels[0].Name);
                    newUI.Run();

                    // When the new ManageUI exits, terminate the entire process to prevent
                    // returning to the old shutdown TUI. The new ManageUI has taken over.
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    // Remove cancel handler
                    if (cancelHandler != null)
                    {
                        Console.CancelKeyPress -= cancelHandler;
                    }

                    // Check if this is actually a cancellation wrapped in another exception
                    var baseEx = ex;
                    while (baseEx != null)
                    {
                        if (baseEx is OperationCanceledException || baseEx is TaskCanceledException)
                        {
                            // This was a cancellation, just return to manage
                            Console.WriteLine("\nPress any key to return to manage view...");
                            Console.ReadKey(true);

                            // Force cleanup before reinitializing Terminal.Gui
                            GC.Collect();
                            GC.WaitForPendingFinalizers();
                            System.Threading.Thread.Sleep(100);

                            // Create a new ManageUI instance to avoid Terminal.Gui state issues
                            new ManageUI(_program, _destination, selectedModels[0].Name).Run();

                            // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                            Environment.Exit(0);
                        }
                        baseEx = baseEx.InnerException;
                    }

                    // Not a cancellation, show the error
                    Console.WriteLine($"\nError: {ex.Message}");
                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    Console.WriteLine("Reinitializing manage view...");

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination, selectedModels[0].Name).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
                finally
                {
                    _program.ThrowOnError = false; // Reset flag
                    cts.Dispose();
                }
            };

            // Allow Enter key on text fields to trigger copy
            if (destNameField != null)
            {
                destNameField.KeyPress += (e) =>
                {
                    if (e.KeyEvent.Key == Key.Enter)
                    {
                        e.Handled = true;
                        performCopy();
                    }
                };
            }

            serverField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter && e.KeyEvent.Key != Key.Tab)
                {
                    e.Handled = true;
                    performCopy();
                }
            };

            throttleField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    performCopy();
                }
            };

            copyButton.Clicked += performCopy;
            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(copyButton);
            dialog.Add(cancelButton);

            Application.Run(dialog);
        }

        // Action: Rename model
        private void ExecuteRename()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            // Create input dialog for new name
            var dialog = new Dialog($"Rename {model.Name}", 60, 10);

            var label = new Label("Enter new name:")
            {
                X = 1,
                Y = 1
            };
            dialog.Add(label);

            var newNameField = new TextField(model.Name)
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            };
            EnableClipboardPaste(newNameField);
            dialog.Add(newNameField);

            var renameButton = new Button("Rename")
            {
                X = Pos.Center() - 12,
                Y = 5
            };
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 5
            };

            // Action to perform rename (shared by button and Enter key)
            Action performRename = () =>
            {
                var newName = newNameField.Text.ToString();
                if (string.IsNullOrWhiteSpace(newName))
                {
                    MessageBox.ErrorQuery("Error", "New name cannot be empty", "OK");
                    return;
                }

                if (newName == model.Name)
                {
                    MessageBox.ErrorQuery("Error", "New name must be different", "OK");
                    return;
                }

                Application.RequestStop();

                try
                {
                    string serverUrl = string.IsNullOrEmpty(_destination)
                        ? "http://localhost:11434"
                        : _destination;

                    using var httpClient = new HttpClient
                    {
                        BaseAddress = new Uri(serverUrl),
                        Timeout = TimeSpan.FromMinutes(5)
                    };

                    // Send copy request (rename is copy + delete)
                    var copyRequest = new
                    {
                        source = model.Name,
                        destination = newName
                    };

                    var jsonContent = JsonSerializer.Serialize(copyRequest);
                    var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                    var response = httpClient.PostAsync("/api/copy", httpContent).Result;
                    response.EnsureSuccessStatusCode();

                    // Delete original
                    var deleteRequest = new
                    {
                        name = model.Name
                    };

                    jsonContent = JsonSerializer.Serialize(deleteRequest);
                    httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                    var deleteMessage = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
                    {
                        Content = httpContent
                    };

                    response = httpClient.SendAsync(deleteMessage).Result;
                    response.EnsureSuccessStatusCode();

                    // Refresh model list
                    LoadModels();

                    MessageBox.Query("Success", $"Renamed '{model.Name}' to '{newName}'", "OK");
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to rename model: {ex.Message}", "OK");
                }
            };

            // Wire up the rename button
            renameButton.Clicked += performRename;

            // Allow Enter key on text field to trigger rename
            newNameField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    performRename();
                }
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(renameButton);
            dialog.Add(cancelButton);

            Application.Run(dialog);
        }

        // Action: Run/chat with model
        private void ExecuteRun()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            // Create dialog for chat parameters
            var dialog = new Dialog($"Run/Chat - {model.Name}", 70, 22);

            var yPos = 1;

            // Format field
            var formatLabel = new Label("Format (--format):")
            {
                X = 1,
                Y = yPos
            };
            var formatField = new TextField("")
            {
                X = 25,
                Y = yPos,
                Width = 40
            };
            dialog.Add(formatLabel);
            dialog.Add(formatField);
            yPos += 2;

            // KeepAlive field
            var keepAliveLabel = new Label("KeepAlive (--keepalive):")
            {
                X = 1,
                Y = yPos
            };
            var keepAliveField = new TextField("")
            {
                X = 25,
                Y = yPos,
                Width = 40
            };
            dialog.Add(keepAliveLabel);
            dialog.Add(keepAliveField);
            yPos += 2;

            // Dimensions field
            var dimensionsLabel = new Label("Dimensions (--dimensions):")
            {
                X = 1,
                Y = yPos
            };
            var dimensionsField = new TextField("")
            {
                X = 25,
                Y = yPos,
                Width = 40
            };
            dialog.Add(dimensionsLabel);
            dialog.Add(dimensionsField);
            yPos += 2;

            // Think dropdown
            var thinkLabel = new Label("Think (--think):")
            {
                X = 1,
                Y = yPos
            };
            var thinkOptions = new List<string> { "(use model default)", "true", "false", "high", "medium", "low" };
            var thinkCombo = new ComboBox()
            {
                X = 25,
                Y = yPos,
                Width = 40,
                Height = 6
            };
            thinkCombo.SetSource(thinkOptions);
            thinkCombo.SelectedItem = 0;
            dialog.Add(thinkLabel);
            dialog.Add(thinkCombo);
            yPos += 2;

            // Checkboxes
            var noWordWrapCheck = new CheckBox("NoWordWrap (--no-wordwrap)")
            {
                X = 1,
                Y = yPos,
                Checked = false
            };
            dialog.Add(noWordWrapCheck);
            yPos++;

            var verboseCheck = new CheckBox("Verbose (--verbose)")
            {
                X = 1,
                Y = yPos,
                Checked = false
            };
            dialog.Add(verboseCheck);
            yPos++;

            var hideThinkingCheck = new CheckBox("HideThinking (--hide-thinking)")
            {
                X = 1,
                Y = yPos,
                Checked = false
            };
            dialog.Add(hideThinkingCheck);
            yPos++;

            var insecureCheck = new CheckBox("Insecure (--insecure)")
            {
                X = 1,
                Y = yPos,
                Checked = false
            };
            dialog.Add(insecureCheck);
            yPos++;

            var truncateCheck = new CheckBox("Truncate (--truncate)")
            {
                X = 1,
                Y = yPos,
                Checked = true  // Default enabled
            };
            dialog.Add(truncateCheck);
            yPos += 2;

            // Buttons
            var startButton = new Button("Start Chat")
            {
                X = Pos.Center() - 15,
                Y = yPos
            };
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 5,
                Y = yPos
            };

            // Action to start chat
            Action startChat = () =>
            {
                Application.RequestStop();

                // Exit TUI to show console output
                _isNormalExit = true;
                Application.Shutdown();

                // Build command arguments
                var args = new List<string>();

                var format = formatField.Text.ToString()?.Trim();
                if (!string.IsNullOrEmpty(format))
                    args.Add($"--format \"{format}\"");

                var keepAlive = keepAliveField.Text.ToString()?.Trim();
                if (!string.IsNullOrEmpty(keepAlive))
                    args.Add($"--keepalive {keepAlive}");

                var dimensionsStr = dimensionsField.Text.ToString()?.Trim();
                int? dimensions = null;
                if (!string.IsNullOrEmpty(dimensionsStr) && int.TryParse(dimensionsStr, out int dimValue))
                {
                    dimensions = dimValue;
                    args.Add($"--dimensions {dimValue}");
                }

                if (thinkCombo.SelectedItem > 0)
                    args.Add($"--think {thinkOptions[thinkCombo.SelectedItem]}");

                if (noWordWrapCheck.Checked)
                    args.Add("--no-wordwrap");

                if (verboseCheck.Checked)
                    args.Add("--verbose");

                if (hideThinkingCheck.Checked)
                    args.Add("--hide-thinking");

                if (insecureCheck.Checked)
                    args.Add("--insecure");

                if (truncateCheck.Checked)
                    args.Add("--truncate");

                if (!string.IsNullOrEmpty(_destination))
                    args.Add($"-d {_destination}");

                // Start chat session
                try
                {
                    var runArgs = new RunArgs
                    {
                        ModelName = model.Name ?? "",
                        Destination = _destination ?? "",
                        Format = format ?? "",
                        KeepAlive = keepAlive ?? "",
                        NoWordWrap = noWordWrapCheck.Checked,
                        Verbose = verboseCheck.Checked,
                        Dimensions = dimensions,
                        HideThinking = hideThinkingCheck.Checked,
                        Insecure = insecureCheck.Checked,
                        Think = thinkCombo.SelectedItem > 0 ? thinkOptions[thinkCombo.SelectedItem] ?? "" : "",
                        Truncate = truncateCheck.Checked
                    };

                    _program.Run(runArgs).GetAwaiter().GetResult();

                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination, model.Name).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination, model.Name).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
            };

            startButton.Clicked += startChat;
            cancelButton.Clicked += () => Application.RequestStop();

            // Add Enter key handlers to all text fields
            formatField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    startChat();
                }
            };

            keepAliveField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    startChat();
                }
            };

            dimensionsField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    startChat();
                }
            };

            dialog.Add(startButton);
            dialog.Add(cancelButton);

            Application.Run(dialog);
        }

        // Action: Show model info
        private void ExecuteShow()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            // Create selection dialog
            var dialog = new Dialog($"Show Info - {model.Name}", 50, 14);

            var label = new Label("Select information to display:")
            {
                X = 1,
                Y = 1
            };
            dialog.Add(label);

            var options = new List<string>
            {
                "License",
                "Modelfile",
                "Parameters",
                "System",
                "Template"
            };

            var listView = new ListView(options)
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2,
                Height = 6
            };
            dialog.Add(listView);

            var showButton = new Button("Show")
            {
                X = Pos.Center() - 10,
                Y = 9
            };
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 9
            };

            showButton.Clicked += () =>
            {
                var selectedIndex = listView.SelectedItem;
                if (selectedIndex < 0 || selectedIndex >= options.Count) return;

                var selectedOption = options[selectedIndex];
                Application.RequestStop();

                try
                {
                    string serverUrl = string.IsNullOrEmpty(_destination)
                        ? "http://localhost:11434"
                        : _destination;

                    using var httpClient = new HttpClient
                    {
                        BaseAddress = new Uri(serverUrl),
                        Timeout = TimeSpan.FromSeconds(30)
                    };

                    var showRequest = new
                    {
                        name = model.Name,
                        verbose = false
                    };

                    var jsonContent = JsonSerializer.Serialize(showRequest);
                    var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                    var response = httpClient.PostAsync("/api/show", httpContent).Result;
                    response.EnsureSuccessStatusCode();

                    string json = response.Content.ReadAsStringAsync().Result;
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Extract the requested field
                    string content = "";
                    switch (selectedOption.ToLowerInvariant())
                    {
                        case "license":
                            if (root.TryGetProperty("license", out var licenseElement))
                                content = licenseElement.GetString() ?? "(No license information)";
                            else
                                content = "(No license information)";
                            break;
                        case "modelfile":
                            if (root.TryGetProperty("modelfile", out var modelfileElement))
                                content = modelfileElement.GetString() ?? "(No modelfile)";
                            else
                                content = "(No modelfile)";
                            break;
                        case "parameters":
                            if (root.TryGetProperty("parameters", out var parametersElement))
                                content = parametersElement.GetString() ?? "(No parameters)";
                            else
                                content = "(No parameters)";
                            break;
                        case "system":
                            if (root.TryGetProperty("system", out var systemElement))
                                content = systemElement.GetString() ?? "(No system prompt)";
                            else
                                content = "(No system prompt)";
                            break;
                        case "template":
                            if (root.TryGetProperty("template", out var templateElement))
                                content = templateElement.GetString() ?? "(No template)";
                            else
                                content = "(No template)";
                            break;
                    }

                    // Show content in a scrollable dialog
                    var contentDialog = new Dialog($"{selectedOption} - {model.Name}", 75, 20);

                    var textView = new TextView()
                    {
                        X = 0,
                        Y = 0,
                        Width = Dim.Fill(),
                        Height = Dim.Fill(1),
                        ReadOnly = true,
                        Text = content
                    };
                    contentDialog.Add(textView);

                    var closeButton = new Button("Close")
                    {
                        X = Pos.Center(),
                        Y = Pos.Bottom(contentDialog) - 2
                    };
                    closeButton.Clicked += () => Application.RequestStop();
                    contentDialog.Add(closeButton);

                    Application.Run(contentDialog);
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to get model info: {ex.Message}", "OK");
                }
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(showButton);
            dialog.Add(cancelButton);

            Application.Run(dialog);
        }

        // Action: Delete model(s)
        private void ExecuteDelete()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            // Get selected models (checked) or current model if none checked
            var selectedModels = _filteredModels.Where(m => m.IsSelected).ToList();
            bool isBatchDelete = selectedModels.Count > 0;

            if (!isBatchDelete)
            {
                var index = _modelListView.SelectedItem;
                if (index < 0 || index >= _filteredModels.Count) return;
                selectedModels = new List<ManageModelInfo> { _filteredModels[index] };
            }

            // Confirm deletion - show all model names for batch delete
            string confirmMessage = isBatchDelete
                ? $"Are you sure you want to delete {selectedModels.Count} models?\n\n{string.Join("\n", selectedModels.Select(m => m.Name))}\n\nThis action cannot be undone."
                : $"Are you sure you want to delete '{selectedModels[0].Name}'?\n\nThis action cannot be undone.";

            string dialogTitle = isBatchDelete ? $"Confirm Delete ({selectedModels.Count} models)" : "Confirm Delete";
            var result = MessageBox.Query(dialogTitle, confirmMessage, "Cancel", "Delete");

            if (result == 1)  // Delete button was pressed
            {
                try
                {
                    string serverUrl = string.IsNullOrEmpty(_destination)
                        ? "http://localhost:11434"
                        : _destination;

                    using var httpClient = new HttpClient
                    {
                        BaseAddress = new Uri(serverUrl),
                        Timeout = TimeSpan.FromSeconds(30)
                    };

                    int successCount = 0;
                    int failCount = 0;
                    string? lastError = null;

                    foreach (var model in selectedModels)
                    {
                        try
                        {
                            // Send delete request
                            var deleteRequest = new
                            {
                                name = model.Name
                            };

                            var jsonContent = JsonSerializer.Serialize(deleteRequest);
                            var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                            var requestMessage = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
                            {
                                Content = httpContent
                            };

                            var response = httpClient.SendAsync(requestMessage).Result;
                            response.EnsureSuccessStatusCode();
                            successCount++;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            lastError = ex.Message;
                        }
                    }

                    // Refresh model list
                    _selectedModelName = null;
                    LoadModels();

                    // Show result
                    if (failCount == 0)
                    {
                        string successMsg = isBatchDelete
                            ? $"{successCount} model(s) deleted successfully"
                            : $"{selectedModels[0].Name} deleted successfully";
                        MessageBox.Query("Success", successMsg, "OK");
                    }
                    else if (successCount == 0)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to delete model(s): {lastError}", "OK");
                    }
                    else
                    {
                        MessageBox.Query("Partial Success", $"{successCount} deleted, {failCount} failed\nLast error: {lastError}", "OK");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to delete model(s): {ex.Message}", "OK");
                }
            }
        }

        // Action: Update model(s)
        private void ExecuteUpdate()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            // Get selected models (checked) or current model if none checked
            var selectedModels = _filteredModels.Where(m => m.IsSelected).ToList();
            bool isBatchUpdate = selectedModels.Count > 0;

            if (!isBatchUpdate)
            {
                var index = _modelListView.SelectedItem;
                if (index < 0 || index >= _filteredModels.Count) return;
                selectedModels = new List<ManageModelInfo> { _filteredModels[index] };
            }

            // Confirm update
            string confirmMessage = isBatchUpdate
                ? $"Update {selectedModels.Count} models to the latest versions?"
                : $"Update '{selectedModels[0].Name}' to the latest version?";

            var result = MessageBox.Query("Confirm Update", confirmMessage, "Cancel", "Update");

            if (result == 1)  // Update button was pressed
            {
                // Exit TUI to show console output
                _isNormalExit = true;
                Application.RequestStop();
                Application.Shutdown();

                // Update models with console output
                try
                {
                    Console.WriteLine($"\nUpdating {selectedModels.Count} model(s)...\n");

                    foreach (var model in selectedModels)
                    {
                        try
                        {
                            _program.ActionUpdate(model.Name ?? "", _destination ?? "");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"✗ Failed to update {model.Name}: {ex.Message}");
                        }
                    }

                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination, selectedModels[0].Name).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination, selectedModels[0].Name).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
            }
        }

        // Action: Pull new model
        private void ExecutePull()
        {
            // Create input dialog for model name
            var dialog = new Dialog("Pull Model", 60, 10);

            var label = new Label("Enter model name (e.g., llama3, mistral:7b):")
            {
                X = 1,
                Y = 1
            };
            dialog.Add(label);

            var modelNameField = new TextField("")
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2
            };
            EnableClipboardPaste(modelNameField);
            dialog.Add(modelNameField);

            var pullButton = new Button("Pull")
            {
                X = Pos.Center() - 10,
                Y = 5
            };
            var cancelButton = new Button("Cancel")
            {
                X = Pos.Center() + 2,
                Y = 5
            };

            Action performPull = () =>
            {
                var modelName = modelNameField.Text.ToString()?.Trim();
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    MessageBox.ErrorQuery("Error", "Model name cannot be empty", "OK");
                    return;
                }

                // Auto-add :latest if no tag specified
                if (!modelName.Contains(':'))
                {
                    modelName += ":latest";
                }

                // Validate model exists using Ollama registry API
                try
                {
                    using var httpClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    // Parse model name and tag
                    var parts = modelName.Split(':');
                    var model = parts[0];
                    var tag = parts.Length > 1 ? parts[1] : "latest";

                    // Build the registry URL based on whether model has a path (user/model) or not (library/model)
                    string registryUrl;
                    if (model.Contains('/'))
                    {
                        // User model: https://registry.ollama.ai/v2/user/model/manifests/tag
                        registryUrl = $"https://registry.ollama.ai/v2/{model}/manifests/{tag}";
                    }
                    else
                    {
                        // Library model: https://registry.ollama.ai/v2/library/model/manifests/tag
                        registryUrl = $"https://registry.ollama.ai/v2/library/{model}/manifests/{tag}";
                    }

                    Console.WriteLine($"Validating model exists...");
                    var checkResponse = httpClient.GetAsync(registryUrl).Result;
                    var responseContent = checkResponse.Content.ReadAsStringAsync().Result;

                    // Check for manifest unknown error in JSON response
                    if (responseContent.Contains("MANIFEST_UNKNOWN") || checkResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        MessageBox.ErrorQuery("Error", $"Model '{modelName}' not found on Ollama.\n\nPlease check the model name and try again.", "OK");
                        return;
                    }

                    if (!checkResponse.IsSuccessStatusCode)
                    {
                        MessageBox.ErrorQuery("Error", $"Failed to validate model (HTTP {(int)checkResponse.StatusCode}).\n\nPlease check your internet connection.", "OK");
                        return;
                    }

                    // Verify response contains valid manifest (has schemaVersion field)
                    if (!responseContent.Contains("schemaVersion"))
                    {
                        MessageBox.ErrorQuery("Error", $"Invalid response from Ollama registry.\n\nPlease try again later.", "OK");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to validate model: {ex.Message}\n\nPlease check your internet connection.", "OK");
                    return;
                }

                Application.RequestStop();

                // Exit TUI to show console output
                _isNormalExit = true;
                Application.Shutdown();

                // Pull model with console output
                try
                {
                    Console.WriteLine($"\nPulling model '{modelName}'...\n");

                    _program.ActionPull(modelName, _destination ?? "");

                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    // Select the newly pulled model
                    new ManageUI(_program, _destination, modelName).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    Console.WriteLine("\nPress any key to return to manage view...");
                    Console.ReadKey(true);

                    // Force cleanup before reinitializing Terminal.Gui
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    System.Threading.Thread.Sleep(100);

                    // Create a new ManageUI instance to avoid Terminal.Gui state issues
                    new ManageUI(_program, _destination).Run();

                    // After the new TUI exits, we need to exit this entire method to prevent returning to dead TUI
                    Environment.Exit(0);
                }
            };

            pullButton.Clicked += performPull;

            // Add Enter key support
            modelNameField.KeyPress += (e) =>
            {
                if (e.KeyEvent.Key == Key.Enter)
                {
                    e.Handled = true;
                    performPull();
                }
            };

            cancelButton.Clicked += () => Application.RequestStop();

            dialog.Add(pullButton);
            dialog.Add(cancelButton);

            Application.Run(dialog);
        }

        // Action: Load model(s) into memory
        private void ExecuteLoad()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            try
            {
                string serverUrl = string.IsNullOrEmpty(_destination)
                    ? "http://localhost:11434"
                    : _destination;

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    Timeout = TimeSpan.FromMinutes(2)
                };

                // Send chat request with keep_alive to preload the model without unloading others
                var preloadRequest = new
                {
                    model = model.Name,
                    messages = new object[] { },
                    keep_alive = "5m",
                    stream = false
                };

                var jsonContent = JsonSerializer.Serialize(preloadRequest);
                var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                {
                    Content = httpContent
                };

                var response = httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead).Result;
                response.EnsureSuccessStatusCode();

                // Consume the response to complete the preload
                response.Content.ReadAsStringAsync().Wait();

                // Refresh running status
                FetchRunningStatus();
                UpdateModelList();

                MessageBox.Query("Success", $"{model.Name} loaded into memory", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to load model: {ex.Message}", "OK");
            }
        }

        // Action: Unload model(s) from memory
        private void ExecuteUnload()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            try
            {
                string serverUrl = string.IsNullOrEmpty(_destination)
                    ? "http://localhost:11434"
                    : _destination;

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                // Send generate request with keep_alive=0 to unload
                var unloadRequest = new
                {
                    model = model.Name,
                    keep_alive = 0
                };

                var jsonContent = JsonSerializer.Serialize(unloadRequest);
                var httpContent = new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json");

                var response = httpClient.PostAsync("/api/generate", httpContent).Result;
                response.EnsureSuccessStatusCode();

                // Refresh running status
                FetchRunningStatus();
                UpdateModelList();

                MessageBox.Query("Success", $"{model.Name} unloaded from memory", "OK");
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to unload model: {ex.Message}", "OK");
            }
        }

        // Action: Show process status
        private void ExecutePs()
        {
            try
            {
                // Fetch running models directly
                string serverUrl = string.IsNullOrEmpty(_destination)
                    ? "http://localhost:11434"
                    : _destination;

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = httpClient.GetAsync("/api/ps").Result;
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.ErrorQuery("Error", $"Failed to get process status (HTTP {response.StatusCode})", "OK");
                    return;
                }

                string json = response.Content.ReadAsStringAsync().Result;
                var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                if (status?.Models == null || status.Models.Count == 0)
                {
                    MessageBox.Query("Process Status", "No models currently loaded in memory", "OK");
                    return;
                }

                // Build display text in same format as CLI
                var lines = new List<string>();
                lines.Add("Loaded Models:");
                lines.Add(new string('-', 135));
                lines.Add($"{"NAME",-30} {"ID",-15} {"SIZE",-25} {"VRAM USAGE",-15} {"CONTEXT",-10} {"UNTIL",-30}");
                lines.Add(new string('-', 135));

                foreach (var model in status.Models)
                {
                    var name = TruncateString(model.Name, 30);
                    var id = GetShortDigest(model.Digest);
                    var size = FormatModelSize(model.Size, model.Details?.ParameterSize);

                    // Add percentage if VRAM usage is less than total size
                    string vramUsage;
                    if (model.SizeVram < model.Size && model.Size > 0)
                    {
                        double percentage = ((double)model.SizeVram / model.Size) * 100;
                        vramUsage = $"{FormatBytes(model.SizeVram)} ({percentage:F0}%)";
                    }
                    else
                    {
                        vramUsage = FormatBytes(model.SizeVram);
                    }

                    var context = model.ContextLength > 0 ? model.ContextLength.ToString() : "N/A";
                    var until = FormatUntil(model.ExpiresAt);

                    lines.Add($"{name,-30} {id,-15} {size,-25} {vramUsage,-15} {context,-10} {until,-30}");
                }

                lines.Add(new string('-', 135));

                // Show in dialog
                var dialog = new Dialog("Process Status", 140, 20);

                var textView = new TextView()
                {
                    X = 0,
                    Y = 0,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                    ReadOnly = true,
                    Text = string.Join("\n", lines)
                };
                dialog.Add(textView);

                var closeButton = new Button("Close")
                {
                    X = Pos.Center(),
                    Y = Pos.Bottom(dialog) - 2
                };
                closeButton.Clicked += () => Application.RequestStop();
                dialog.Add(closeButton);

                Application.Run(dialog);
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Error", $"Failed to get process status: {ex.Message}", "OK");
            }
        }

        // Show extended information about the selected model
        private void ShowExtendedInfo()
        {
            if (_modelListView == null || _filteredModels.Count == 0) return;

            var index = _modelListView.SelectedItem;
            if (index < 0 || index >= _filteredModels.Count) return;

            var model = _filteredModels[index];

            // Fetch extended info if not already loaded
            if (!model.ExtendedInfoLoaded)
            {
                FetchExtendedInfo(model);
            }

            // Build info text
            var lines = new List<string>();
            lines.Add($"Model: {model.Name}");
            lines.Add($"ID: {model.ShortId}");
            lines.Add($"Size: {model.SizeFormatted}");
            lines.Add($"Modified: {model.ModifiedFormatted}");
            lines.Add("");

            if (!string.IsNullOrEmpty(model.Family))
            {
                lines.Add($"Family: {model.Family}");
            }
            if (!string.IsNullOrEmpty(model.Quantization))
            {
                lines.Add($"Quantization: {model.Quantization}");
            }
            if (model.IsLoaded)
            {
                lines.Add("Status: LOADED IN MEMORY");
            }
            lines.Add("");

            if (model.ExtendedInfoLoaded)
            {
                lines.Add("--- Parameters ---");
                if (model.NumCtx.HasValue)
                    lines.Add($"Context: {model.NumCtx.Value}");
                if (model.Temperature.HasValue)
                    lines.Add($"Temperature: {model.Temperature.Value}");
                if (model.TopP.HasValue)
                    lines.Add($"Top P: {model.TopP.Value}");
                if (model.TopK.HasValue)
                    lines.Add($"Top K: {model.TopK.Value}");
                if (!string.IsNullOrEmpty(model.Stop))
                    lines.Add($"Stop: {model.Stop}");
            }
            else
            {
                lines.Add("(Extended info could not be loaded)");
            }

            // Create a dialog to show the info
            var dialog = new Dialog($"Model Info - {model.Name}", 75, 20);

            var textView = new TextView()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
                ReadOnly = true,
                Text = string.Join("\n", lines)
            };
            dialog.Add(textView);

            var closeButton = new Button("Close")
            {
                X = Pos.Center(),
                Y = Pos.Bottom(dialog) - 2
            };
            closeButton.Clicked += () => Application.RequestStop();
            dialog.Add(closeButton);

            Application.Run(dialog);
        }

        // Helper methods for ps command
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
    }
}
