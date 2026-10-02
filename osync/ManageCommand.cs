using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using ByteSizeLib;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TgAttribute = Terminal.Gui.Drawing.Attribute;
using TgColor = Terminal.Gui.Drawing.Color;

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

        // xOllama settings (the "xollama" field of /api/show); null on Ollama or when the model states none
        public List<(string Path, string Value)>? XOllamaSettings { get; set; }

        // State
        public bool IsSelected { get; set; }
        public bool IsLoaded { get; set; }
        public bool ExtendedInfoLoaded { get; set; }
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

    /// <summary>The servers manage switches between with Ctrl+Left / Ctrl+Right.</summary>
    internal static class ManageServers
    {
        /// <summary>
        /// The local server first (Url null), then, with Ollama and xOllama side by side, the other one, then the
        /// aliases chosen for manage (manage.servers). Unknown aliases, duplicates and aliases pointing at
        /// <paramref name="localUrl"/> (when given) are left out.
        /// </summary>
        public static List<(string Name, string? Url)> Targets(OsyncSettings settings, string? localUrl)
        {
            var targets = new List<(string Name, string? Url)> { ("local", null) };
            var names = new List<string>();
            if (settings.Server.Both == true)
                names.Add(settings.ConfiguredFlavor == ServerFlavor.XOllama ? ServerSetup.OllamaAlias : ServerSetup.XOllamaAlias);
            if (settings.Manage.Servers != null)
                names.AddRange(settings.Manage.Servers);

            foreach (var name in names)
            {
                var key = settings.Aliases.Keys.FirstOrDefault(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase));
                if (key == null || targets.Any(t => string.Equals(t.Name, key, StringComparison.OrdinalIgnoreCase))) continue;
                var url = settings.Aliases[key].TrimEnd('/');
                if (localUrl != null && SameServer(url, localUrl)) continue;
                if (targets.Any(t => t.Url != null && SameServer(t.Url, url))) continue;
                targets.Add((key, url));
            }
            return targets;
        }

        public static bool SameServer(string a, string b) =>
            Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub) &&
            string.Equals(ua.Authority, ub.Authority, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Names of the manage sort orders, as stored in the settings file (manage.sort).</summary>
    internal static class ManageSortOrders
    {
        public static readonly (SortOrder Order, string Name, string Description)[] All =
        {
            (SortOrder.AlphabeticalAsc, "name+", "name, A to Z"),
            (SortOrder.AlphabeticalDesc, "name-", "name, Z to A"),
            (SortOrder.SizeDesc, "size-", "size, largest first"),
            (SortOrder.SizeAsc, "size+", "size, smallest first"),
            (SortOrder.CreatedDesc, "created-", "newest first"),
            (SortOrder.CreatedAsc, "created+", "oldest first")
        };

        /// <summary>Sort order from a name (name+, size-, created-, ... also name, size, newest, oldest); null if unknown.</summary>
        public static SortOrder? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
        {
            "name+" or "name" or "name-asc" => SortOrder.AlphabeticalAsc,
            "name-" or "name-desc" => SortOrder.AlphabeticalDesc,
            "size-" or "size" or "size-desc" or "largest" => SortOrder.SizeDesc,
            "size+" or "size-asc" or "smallest" => SortOrder.SizeAsc,
            "created-" or "created" or "newest" or "created-desc" => SortOrder.CreatedDesc,
            "created+" or "oldest" or "created-asc" => SortOrder.CreatedAsc,
            _ => null
        };

        public static string Name(SortOrder order) => All.First(o => o.Order == order).Name;
    }

    /// <summary>
    /// Full-screen model manager (Terminal.Gui 2). Operations that print to the console (copy, run, update,
    /// pull) close the TUI, run on the plain console and then reopen it: <see cref="Run"/> loops over
    /// "TUI session → pending console action" until the user quits.
    /// </summary>
    public class ManageUI
    {
        private const string LoadedMarker = "●";

        private readonly OsyncProgram _program;
        private string? _destination;

        // Servers of Ctrl+Left / Ctrl+Right: the local server (Url null), then the aliases chosen for manage
        private List<(string Name, string? Url)> _targets = new() { ("local", null) };
        private int _targetIndex;
        private List<ManageModelInfo> _allModels = new();
        private List<ManageModelInfo> _filteredModels = new();
        private string _filterText = "";
        private SortOrder _currentSortOrder = SortOrder.AlphabeticalAsc;

        // Theme chosen by the user, and the same theme adapted to the terminal's color depth
        private OsyncTheme _theme = Themes.Default;
        private OsyncTheme _drawTheme = Themes.Default;
        private ColorDepth _depth = ColorDepth.TrueColor;

        // Dynamic column widths
        private int _sizeColumnWidth = 6;
        private int _paramsColumnWidth = 6;
        private int _quantColumnWidth = 6;
        private int _familyColumnWidth = 6;
        private int _modifiedColumnWidth = 3;
        private int _idColumnWidth = 12;
        private string _serverLabel = "";

        // Session state for copy operation
        private string? _lastCopyDestinationServer = null;
        private readonly List<string> _destinationHistory = new();

        // Model to put the cursor on at the next list refresh
        private string? _selectedModelName;

        // Transient message in the top bar
        private string _status = "";
        private object? _statusTimer;

        // What to do after the current TUI session closes
        private Func<CancellationToken, string?>? _pendingConsoleAction;
        private bool _restartRequested;
        private string? _startupError;

        // Terminal.Gui objects of the current session
        private IApplication? _app;
        private Runnable? _top;
        private SegmentBar? _topBar;
        private SegmentBar? _header;
        private ListView? _modelListView;
        private ModelListSource? _source;
        private SegmentBar? _bottomBar;

        public ManageUI(OsyncProgram program, string? destination, string? selectedModelName = null)
        {
            _program = program ?? throw new ArgumentNullException(nameof(program));
            _destination = destination;
            _selectedModelName = selectedModelName;
        }

        private string ServerUrl => string.IsNullOrEmpty(_destination) ? OllamaServer.LocalUrl : _destination;

        /// <summary>Servers of Ctrl+Left / Ctrl+Right; a -d server that is not among them is added.</summary>
        private void BuildTargets()
        {
            _targets = ManageServers.Targets(OsyncSettings.Current, OllamaServer.LocalUrl);
            _targetIndex = 0;
            if (string.IsNullOrEmpty(_destination)) return;
            if (ManageServers.SameServer(_destination, OllamaServer.LocalUrl))
            {
                _destination = null;
                return;
            }
            var index = _targets.FindIndex(t => t.Url != null && ManageServers.SameServer(t.Url, _destination));
            if (index < 0)
            {
                _targets.Add((new Uri(_destination).Authority, _destination));
                index = _targets.Count - 1;
            }
            _targetIndex = index;
        }

        /// <summary>Ctrl+Left / Ctrl+Right: shows the previous / next server.</summary>
        private void SwitchServer(int step)
        {
            if (_targets.Count < 2)
            {
                SetStatus("One server only: choose more with 'osync setup manage servers'");
                return;
            }
            _targetIndex = (_targetIndex + step + _targets.Count) % _targets.Count;
            _destination = _targets[_targetIndex].Url;
            _selectedModelName = null;
            foreach (var model in _allModels) model.IsSelected = false;
            LoadModels();
            UpdateModelList();
            SetStatus($"Server: {_targets[_targetIndex].Name}");
        }

        // ------------------------------------------------------------------------------------------------
        // Session loop
        // ------------------------------------------------------------------------------------------------

        public void Run()
        {
            _theme = Themes.Find(OsyncSettings.Current.Manage.Theme);
            _currentSortOrder = ManageSortOrders.Parse(OsyncSettings.Current.Manage.Sort) ?? SortOrder.AlphabeticalAsc;
            BuildTargets();

            while (true)
            {
                _pendingConsoleAction = null;
                _restartRequested = false;

                if (!RunTuiSession())
                    return;

                if (_pendingConsoleAction != null)
                {
                    RunConsoleAction(_pendingConsoleAction);
                    continue;
                }

                if (!_restartRequested)
                    return;
            }
        }

        /// <summary>Runs one TUI session. Returns false when Terminal.Gui cannot start.</summary>
        private bool RunTuiSession()
        {
            // Terminal.Gui writes 24-bit or 16 colors; draw the theme in the colors that actually reach the screen
            var depth = ColorSupport.Current;
            var sixteen = Themes.UseSixteenColors(depth, Environment.GetEnvironmentVariable("TERM_PROGRAM"));
            Driver.Force16Colors = sixteen;
            _depth = sixteen && depth != ColorDepth.None ? ColorDepth.Standard16 : depth;
            _drawTheme = Themes.Adapt(_theme, _depth);
            RegisterSchemes();
            ConfigureLook();

            IApplication app;
            try
            {
                app = Application.Create().Init(TuiDriverName());
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: cannot start the manage view: {ex.Message}");
                return false;
            }

            Exception? failure = null;
            try
            {
                _app = app;
                _top = BuildUI();
                LoadModels();
                app.ScreenChanged += OnScreenChanged;
                if (_startupError != null)
                {
                    var message = _startupError;
                    _startupError = null;
                    app.AddTimeout(TimeSpan.FromMilliseconds(50), () =>
                    {
                        MessageBox.ErrorQuery(app, "Error", message, "OK");
                        return false;
                    });
                }
                app.Run(_top);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                app.ScreenChanged -= OnScreenChanged;
                _top?.Dispose();
                _top = null;
                _topBar = null;
                _header = null;
                _modelListView = null;
                _source = null;
                _bottomBar = null;
                _statusTimer = null;
                _app = null;
                app.Dispose();
            }

            if (failure != null)
            {
                // After Dispose, so the message lands on the restored console
                Console.WriteLine($"\nError in manage view: {failure.Message}");
                _pendingConsoleAction = null;
                _restartRequested = false;
            }
            return true;
        }

        /// <summary>
        /// Single-line borders, no shadows and glyphs that common console fonts have (Consolas, the Linux
        /// console), instead of Terminal.Gui's heavy borders, shadows and ⟦ ⟧ ☑ ◉ glyphs.
        /// </summary>
        private static void ConfigureLook()
        {
            DialogSettings.Current = DialogSettings.Current with { DefaultBorderStyle = LineStyle.Single, DefaultShadow = ShadowStyles.None };
            MessageBoxSettings.Current = MessageBoxSettings.Current with { DefaultBorderStyle = LineStyle.Single };
            ButtonSettings.Current = ButtonSettings.Current with { DefaultShadow = ShadowStyles.None };
            GlyphSettings.Current = GlyphSettings.Current with
            {
                LeftBracket = (Rune)'[',
                RightBracket = (Rune)']',
                CheckStateChecked = (Rune)'■',
                CheckStateUnChecked = (Rune)'□',
                Selected = (Rune)'●',
                UnSelected = (Rune)'○'
            };
        }

        /// <summary>
        /// Terminal.Gui driver: OSYNC_TUI_DRIVER (ansi, dotnet, windows) when set; the Windows console driver on
        /// Windows versions without VT support; otherwise Terminal.Gui's default (ANSI).
        /// </summary>
        private static string? TuiDriverName()
        {
            var configured = Environment.GetEnvironmentVariable("OSYNC_TUI_DRIVER");
            if (!string.IsNullOrWhiteSpace(configured))
                return configured.Trim().ToLowerInvariant();
            if (OperatingSystem.IsWindows() && !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
                return "windows";
            return null;
        }

        /// <summary>Leaves the TUI, runs <paramref name="action"/> on the console, then comes back.</summary>
        private void RequestConsoleAction(Func<CancellationToken, string?> action)
        {
            _pendingConsoleAction = action;
            _app?.RequestStop();
        }

        /// <summary>
        /// Runs a console operation (Ctrl+C cancels it and returns to manage). The action returns the model to
        /// select when the view comes back.
        /// </summary>
        private void RunConsoleAction(Func<CancellationToken, string?> action)
        {
            using var cts = new CancellationTokenSource();
            ConsoleCancelEventHandler cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                if (!cts.IsCancellationRequested)
                {
                    Console.WriteLine("\n\nOperation cancelled. Returning to manage view...");
                    cts.Cancel();
                }
            };
            Console.CancelKeyPress += cancelHandler;
            _program.ThrowOnError = true;
            try
            {
                var select = action(cts.Token);
                if (select != null) _selectedModelName = select;
            }
            catch (Exception ex)
            {
                if (!IsCancellation(ex))
                    Out.Error($"\n{ex.Message}");
            }
            finally
            {
                _program.ThrowOnError = false;
                Console.CancelKeyPress -= cancelHandler;
            }

            Console.WriteLine("\nPress any key to return to manage view...");
            try
            {
                if (!Console.IsInputRedirected) Console.ReadKey(true);
            }
            catch (InvalidOperationException)
            {
                // No console input
            }
        }

        private static bool IsCancellation(Exception? ex)
        {
            for (; ex != null; ex = ex.InnerException)
                if (ex is OperationCanceledException) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------------
        // Layout and colors
        // ------------------------------------------------------------------------------------------------

        private Runnable BuildUI()
        {
            var top = new Runnable { Width = Dim.Fill(), Height = Dim.Fill() };

            _topBar = new SegmentBar { X = 0, Y = 0, Width = Dim.Fill(), Height = 1 };
            _header = new SegmentBar { X = 0, Y = 1, Width = Dim.Fill(), Height = 1 };

            _source = new ModelListSource(this);
            _modelListView = new ListView
            {
                X = 0,
                Y = 2,
                Width = Dim.Fill(),
                Height = Dim.Fill(1),   // leave room for the bottom bar
                Source = _source,
                KeystrokeNavigator = null   // letters go to the filter, not to type-ahead search
            };
            _modelListView.KeyDown += OnKeyDown;
            _modelListView.Accepting += (_, e) =>
            {
                // Enter or double click: model details
                e.Handled = true;
                ShowExtendedInfo();
            };

            _bottomBar = new SegmentBar { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };

            top.Add(_topBar, _header, _modelListView, _bottomBar);
            ApplyTheme(top);
            _modelListView.SetFocus();
            return top;
        }

        private static TgColor C(Rgb c) => new(c.R, c.G, c.B);
        private static TgAttribute A(Rgb fg, Rgb bg, TextStyle style = TextStyle.None) => new(C(fg), C(bg), style);

        /// <summary>
        /// Registers the theme's schemes under Terminal.Gui's built-in names, so dialogs and message boxes use
        /// the theme (and not whatever a ~/.tui/config.json on the machine defines).
        /// </summary>
        private void RegisterSchemes()
        {
            var t = _drawTheme;
            var selection = A(t.SelectionText, t.SelectionBackground);

            var baseScheme = new Scheme
            {
                Normal = A(t.Text, t.Background),
                HotNormal = A(t.BarAccent, t.Background),
                Focus = selection,
                HotFocus = selection,
                Active = selection,
                HotActive = selection,
                Highlight = A(t.Text, t.AltBackground),
                Editable = A(t.FieldText, t.FieldBackground),
                ReadOnly = A(t.Muted, t.Background),
                Disabled = A(t.Muted, t.Background)
            };

            var dialogScheme = new Scheme
            {
                Normal = A(t.DialogText, t.DialogBackground),
                HotNormal = A(t.BarAccent, t.DialogBackground),
                Focus = selection,
                HotFocus = selection,
                Active = selection,
                HotActive = selection,
                Highlight = selection,
                Editable = A(t.FieldText, t.FieldBackground),
                ReadOnly = A(t.FieldText, t.FieldBackground),
                Disabled = A(t.Muted, t.DialogBackground)
            };

            var errorScheme = dialogScheme with
            {
                Normal = A(t.Error, t.DialogBackground, TextStyle.Bold),
                HotNormal = A(t.Error, t.DialogBackground, TextStyle.Bold)
            };

            SchemeManager.AddScheme(nameof(Schemes.Base), baseScheme);
            SchemeManager.AddScheme(nameof(Schemes.Dialog), dialogScheme);
            SchemeManager.AddScheme(nameof(Schemes.Error), errorScheme);
            SchemeManager.AddScheme(nameof(Schemes.Menu), dialogScheme);
            SchemeManager.AddScheme(nameof(Schemes.Accent), dialogScheme);
        }

        private void ApplyTheme(View? top = null)
        {
            RegisterSchemes();
            var scheme = SchemeManager.GetScheme(nameof(Schemes.Base));
            (top ?? _top)?.SetScheme(scheme);
            _modelListView?.SetScheme(scheme);
            UpdateBars();
            _top?.SetNeedsDraw();
            _modelListView?.SetNeedsDraw();
        }

        private void SetTheme(OsyncTheme theme)
        {
            _theme = theme;
            _drawTheme = Themes.Adapt(theme, _depth);
            ApplyTheme();
        }

        private void OnScreenChanged(object? sender, EventArgs<System.Drawing.Rectangle> e)
        {
            try
            {
                CalculateColumnWidths();
                UpdateBars();
                _modelListView?.SetNeedsDraw();
            }
            catch
            {
                // Ignore resize errors during shutdown
            }
        }

        private int ScreenWidth => _app?.Screen.Width is > 0 and var w ? w : 80;

        // ------------------------------------------------------------------------------------------------
        // Data
        // ------------------------------------------------------------------------------------------------

        // Load models from local or remote server
        private void LoadModels()
        {
            try
            {
                // List through the server's API, so the models and their details (quantization, family,
                // parameter size) always come from the same server. Reading the local models directory is only
                // a fallback when the local server is not reachable: the directory may belong to a different
                // server than the one osync resolves (e.g. both Ollama and xOllama installed).
                var serverUrl = ServerUrl;
                try
                {
                    FetchServerModels(serverUrl);
                    _serverLabel = $"{OllamaServer.DisplayName(OllamaServer.GetFlavor(serverUrl))} @ {new Uri(serverUrl).Authority}";
                }
                catch when (string.IsNullOrEmpty(_destination))
                {
                    FetchLocalModels();
                    _serverLabel = $"{new Uri(serverUrl).Authority} unreachable - local files";
                }

                // Mark loaded models
                FetchRunningStatus();

                // Keep multi-selection across reloads
                CalculateColumnWidths();
                FilterModels();
            }
            catch (Exception ex)
            {
                _allModels = new List<ManageModelInfo>();
                _filteredModels = _allModels;
                _serverLabel = $"{new Uri(ServerUrl).Authority} unreachable";
                UpdateModelList();
                if (_top?.IsRunning == true && _app != null)
                    MessageBox.ErrorQuery(_app, "Error", $"Failed to load models: {ex.Message}", "OK");
                else
                    _startupError = $"Failed to load models: {ex.Message}";
            }
        }

        /// <summary>Reloads the model list, keeping the cursor on <paramref name="select"/> (or the current model).</summary>
        private void ReloadModels(string? select = null)
        {
            _selectedModelName = select ?? CurrentModel()?.Name;
            var checkedNames = _allModels.Where(m => m.IsSelected).Select(m => m.Name).ToHashSet();
            LoadModels();
            foreach (var model in _allModels)
                model.IsSelected = checkedNames.Contains(model.Name);
            UpdateModelList();
        }

        // Calculate optimal column widths based on actual data
        private void CalculateColumnWidths()
        {
            var termWidth = ScreenWidth;

            // Content widths, at least as wide as the column headers
            _sizeColumnWidth = Math.Max(_allModels.Select(m => m.SizeFormatted.Length).DefaultIfEmpty(0).Max(), 6);
            _paramsColumnWidth = Math.Max(_allModels.Select(m => m.ParameterSize.Length).DefaultIfEmpty(0).Max(), 6);
            _quantColumnWidth = Math.Max(_allModels.Select(m => string.IsNullOrEmpty(m.Quantization) ? 7 : m.Quantization.Length).DefaultIfEmpty(0).Max(), 6);
            _familyColumnWidth = Math.Max(_allModels.Select(m => m.Family.Length).DefaultIfEmpty(0).Max(), 6);
            _modifiedColumnWidth = Math.Max(_allModels.Select(m => m.ModifiedFormatted.Length).DefaultIfEmpty(0).Max(), 3);
            _idColumnWidth = 12; // ID is always 12 characters from digest (same as ollama ls)

            // If the terminal is too narrow, reduce column widths proportionally
            const int minNameWidth = 20;
            var fixedWidth = PrefixWidth + ColumnsWidth() + minNameWidth;
            if (termWidth < fixedWidth)
            {
                var availableWidth = termWidth - (PrefixWidth + minNameWidth + 6);
                var currentTotalWidth = ColumnsWidth() - 6;

                if (currentTotalWidth > availableWidth && availableWidth > 20)
                {
                    var scaleFactor = (double)availableWidth / currentTotalWidth;
                    _sizeColumnWidth = Math.Max(4, (int)(_sizeColumnWidth * scaleFactor));
                    _paramsColumnWidth = Math.Max(3, (int)(_paramsColumnWidth * scaleFactor));
                    _quantColumnWidth = Math.Max(4, (int)(_quantColumnWidth * scaleFactor));
                    _familyColumnWidth = Math.Max(4, (int)(_familyColumnWidth * scaleFactor));
                    _modifiedColumnWidth = Math.Max(2, (int)(_modifiedColumnWidth * scaleFactor));
                    _idColumnWidth = Math.Max(6, (int)(_idColumnWidth * scaleFactor));
                }
            }
        }

        /// <summary>"[X] ● " before the name.</summary>
        private const int PrefixWidth = 6;

        /// <summary>Width of the data columns after the name, including the separating spaces.</summary>
        private int ColumnsWidth() =>
            _sizeColumnWidth + _paramsColumnWidth + _quantColumnWidth + _familyColumnWidth + _modifiedColumnWidth + _idColumnWidth + 6;

        private int NameWidth(int width) => Math.Max(20, width - PrefixWidth - ColumnsWidth());

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

                            try
                            {
                                var manifest = ManifestReader.Read<RootManifest>(tagFile);
                                if (manifest?.layers != null)
                                {
                                    totalSize = manifest.layers.Sum(l => l.size);
                                }

                                // Compute SHA256 of manifest file content (same as ollama ls)
                                var hashBytes = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tagFile));
                                modelId = Convert.ToHexString(hashBytes).ToLowerInvariant()[..12];
                            }
                            catch { }

                            _allModels.Add(new ManageModelInfo
                            {
                                Name = fullModelName,
                                ShortId = modelId,
                                FullDigest = modelId,
                                Size = totalSize,
                                SizeFormatted = FormatSize(totalSize),
                                ModifiedAt = fileInfo.LastWriteTime,
                                ModifiedFormatted = GetTimeAgo(fileInfo.LastWriteTime)
                            });
                        }
                    }
                }
            }
        }

        // Fetch models from the server
        private void FetchServerModels(string serverUrl)
        {
            _allModels = new List<ManageModelInfo>();

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(serverUrl),
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

                        _allModels.Add(new ManageModelInfo
                        {
                            Name = model.name ?? "",
                            ShortId = modelId,
                            FullDigest = model.digest ?? "",
                            Size = model.size,
                            SizeFormatted = FormatSize(model.size),
                            ModifiedAt = model.modified_at,
                            ModifiedFormatted = GetTimeAgo(model.modified_at),
                            Quantization = model.details?.quantization_level ?? "",
                            Family = model.details?.family ?? "",
                            ParameterSize = model.details?.parameter_size ?? ""
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to fetch models from {serverUrl}: {ex.Message}", ex);
            }
        }

        private static string FormatSize(long bytes)
        {
            var size = ByteSize.FromBytes(bytes);
            return size.GigaBytes >= 1 ? $"{size.GigaBytes:F1}GB" : $"{size.MegaBytes:F0}MB";
        }

        // Format time ago string (compact for TUI)
        private static string GetTimeAgo(DateTime dateTime)
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
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(new { name = model.Name, verbose = false }),
                    Encoding.UTF8,
                    "application/json");

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
                            model.ParameterSize = paramSize.GetString() ?? "";
                        }
                    }

                    // Extract parameters
                    if (root.TryGetProperty("parameters", out var paramsElement))
                    {
                        var paramsStr = paramsElement.GetString();
                        if (!string.IsNullOrEmpty(paramsStr))
                        {
                            foreach (var line in paramsStr.Split('\n'))
                            {
                                var parts = line.Trim().Split(new[] { ' ' }, 2);
                                if (parts.Length != 2) continue;

                                var value = parts[1].Trim();
                                switch (parts[0].ToLowerInvariant())
                                {
                                    case "num_ctx":
                                        if (int.TryParse(value, out int ctx))
                                            model.NumCtx = ctx;
                                        break;
                                    case "stop":
                                        model.Stop = model.Stop == null ? value : model.Stop + " " + value;
                                        break;
                                    case "temperature":
                                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double temp))
                                            model.Temperature = temp;
                                        break;
                                    case "top_p":
                                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double topP))
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

                    model.XOllamaSettings = root.TryGetProperty("xollama", out var xollama)
                        ? XOllamaTweak.Flatten(xollama)
                        : null;

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
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = httpClient.GetAsync("/api/ps").Result;
                if (response.IsSuccessStatusCode)
                {
                    string json = response.Content.ReadAsStringAsync().Result;
                    var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);
                    var loadedModelNames = status?.Models?.Select(m => m.Name).ToHashSet() ?? new HashSet<string>();
                    foreach (var model in _allModels)
                    {
                        model.IsLoaded = loadedModelNames.Contains(model.Name);
                    }
                }
            }
            catch
            {
                // Silently fail - running status is optional
            }
        }

        // ------------------------------------------------------------------------------------------------
        // List, bars and keys
        // ------------------------------------------------------------------------------------------------

        private ManageModelInfo? CurrentModel()
        {
            var index = _modelListView?.SelectedItem;
            return index is int i && i >= 0 && i < _filteredModels.Count ? _filteredModels[i] : null;
        }

        // Refresh the model list view (keeps the cursor, or moves it to _selectedModelName)
        private void UpdateModelList()
        {
            if (_modelListView == null || _source == null) return;

            var previous = _modelListView.SelectedItem;
            _source.Reset();

            int? target = null;
            if (!string.IsNullOrEmpty(_selectedModelName))
            {
                var found = _filteredModels.FindIndex(m => m.Name == _selectedModelName);
                if (found >= 0) target = found;
                _selectedModelName = null;
            }
            target ??= previous is int p && p < _filteredModels.Count ? p : (_filteredModels.Count > 0 ? 0 : null);
            if (target is int t && t >= 0 && t < _filteredModels.Count)
            {
                _modelListView.SelectedItem = t;
                _modelListView.EnsureSelectedItemVisible();
            }

            UpdateBars();
            _modelListView.SetNeedsDraw();
        }

        private void UpdateBars()
        {
            UpdateTopBar();
            UpdateHeader();
            UpdateBottomBar();
        }

        // Top bar: sort, filter, selection, status on the left; server and version on the right
        private void UpdateTopBar()
        {
            if (_topBar == null) return;
            var t = _drawTheme;

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

            var left = new List<SegmentBar.Segment>
            {
                new("Sorting: ", C(t.BarText)),
                new(sortDisplay, C(t.BarAccent), true)
            };
            if (!string.IsNullOrEmpty(_filterText))
            {
                left.Add(new("  Filter: ", C(t.BarText)));
                left.Add(new(_filterText, C(t.BarAccent), true));
            }
            var marked = _allModels.Count(m => m.IsSelected);
            if (marked > 0)
            {
                left.Add(new("  Selected: ", C(t.BarText)));
                left.Add(new(marked.ToString(), C(t.Checked), true));
            }
            left.Add(new($"  {_filteredModels.Count}/{_allModels.Count} models", C(t.BarText)));
            if (!string.IsNullOrEmpty(_status))
            {
                left.Add(new("  " + _status, C(t.BarAccent), true));
            }

            var right = new List<SegmentBar.Segment>();
            if (_targets.Count > 1)
                right.Add(new($"[{_targetIndex + 1}/{_targets.Count}] {_targets[_targetIndex].Name}: ", C(t.BarText)));
            if (!string.IsNullOrEmpty(_serverLabel))
                right.Add(new(_serverLabel + "  ", C(t.BarAccent)));
            right.Add(new($"osync manage v{OsyncProgram.AppVersion}", C(t.BarText)));

            _topBar.Set(left, right, C(t.BarBackground));
        }

        // Column titles, aligned with the rows
        private void UpdateHeader()
        {
            if (_header == null) return;
            var t = _drawTheme;
            var nameWidth = NameWidth(ScreenWidth);
            var text = new string(' ', PrefixWidth) +
                       Fit("NAME", nameWidth, left: true) + " " +
                       Fit("SIZE", _sizeColumnWidth) + " " +
                       Fit("PARAMS", _paramsColumnWidth) + " " +
                       Fit("QUANT", _quantColumnWidth) + " " +
                       Fit("FAMILY", _familyColumnWidth) + " " +
                       Fit("AGE", _modifiedColumnWidth) + " " +
                       Fit("ID", _idColumnWidth);
            _header.Set(new[] { new SegmentBar.Segment(text, C(t.Muted), true) }, Array.Empty<SegmentBar.Segment>(), C(t.Background));
        }

        // Ctrl+M arrives as Enter in most terminals: rename is on F2 (Ctrl+M still works where it is distinct)
        private static readonly (string Key, string Label)[] Shortcuts =
        {
            ("q", "quit"), ("c", "cp"), ("r", "run"), ("s", "sh"), ("d", "del"), ("u", "upd"), ("p", "pull"),
            ("l", "load"), ("k", "unl"), ("x", "ps"), ("o", "sort"), ("t", "theme"), ("e", "set")
        };

        private const string HelpText =
            "Keys\n" +
            "  Up/Down PgUp/PgDn Home/End   move\n" +
            "  Space                        select / unselect (batch copy, delete, update)\n" +
            "  Enter, Right                 model details\n" +
            "  letters, digits, : - _ . /   filter by name (* = any text)\n" +
            "  Backspace                    remove the last filter character\n" +
            "  Esc                          clear the filter, or exit\n" +
            "  F1                           this help\n" +
            "  F2 (Ctrl+M)                  rename\n" +
            "  Ctrl+Left / Ctrl+Right       previous / next server (osync setup manage servers)\n" +
            "\n" +
            "Ctrl+ actions\n" +
            "  C  copy (same server, or to a remote server)   R  run / chat\n" +
            "  S  show license, modelfile, parameters, ...     D  delete\n" +
            "  U  update from the registry                     P  pull a new model\n" +
            "  L  load into memory                             K  unload from memory\n" +
            "  X  loaded models (ps)                           O  sort order\n" +
            "  T  theme (saved in the settings file)           E  settings: server, colors\n" +
            "  W  tweak: xOllama settings of the model, or of a server on this machine (xollama CLI)\n" +
            "  Q  quit\n" +
            "\n" +
            "In the list: [X] = selected, " + LoadedMarker + " = loaded in memory.";

        private void UpdateBottomBar()
        {
            if (_bottomBar == null) return;
            var t = _drawTheme;
            var left = new List<SegmentBar.Segment>
            {
                new("F1", C(t.BarAccent), true),
                new("=help ", C(t.BarText)),
                new("F2", C(t.BarAccent), true),
                new("=ren  ", C(t.BarText))
            };
            if (_targets.Count > 1)
            {
                left.Add(new("^←/→", C(t.BarAccent), true));
                left.Add(new("=server  ", C(t.BarText)));
            }
            left.Add(new("Ctrl+", C(t.BarText)));
            // Tweak (xOllama's model settings) only where it applies, early enough to fit on narrow terminals
            var xollama = IsXOllamaServer;
            foreach (var (key, label) in Shortcuts)
            {
                left.Add(new(" " + key, C(t.BarAccent), true));
                left.Add(new("=" + label, C(t.BarText)));
                if (key == "s" && xollama)
                {
                    left.Add(new(" w", C(t.BarAccent), true));
                    left.Add(new("=tweak", C(t.BarText)));
                }
            }
            _bottomBar.Set(left, Array.Empty<SegmentBar.Segment>(), C(t.BarBackground));
        }

        /// <summary>Pads or truncates to exactly <paramref name="width"/> columns.</summary>
        internal static string Fit(string text, int width, bool left = false)
        {
            if (width <= 0) return "";
            if (text.Length > width)
                return width > 3 && left ? text[..(width - 3)] + "..." : text[..width];
            return left ? text.PadRight(width) : text.PadLeft(width);
        }

        private void SetStatus(string message)
        {
            _status = message;
            UpdateTopBar();
            if (_app == null) return;
            if (_statusTimer != null) _app.RemoveTimeout(_statusTimer);
            _statusTimer = _app.AddTimeout(TimeSpan.FromSeconds(4), () =>
            {
                _status = "";
                _statusTimer = null;
                UpdateTopBar();
                return false;
            });
        }

        // Keyboard handler of the model list
        private void OnKeyDown(object? sender, Key key)
        {
            // Navigation keys: handled by the ListView
            if (key == Key.CursorUp || key == Key.CursorDown || key == Key.PageUp || key == Key.PageDown ||
                key == Key.Home || key == Key.End)
            {
                return;
            }

            key.Handled = true;

            if (key == Key.Space)
            {
                ToggleSelection();
                return;
            }

            // Escape: clear the filter, or exit with confirmation
            if (key == Key.Esc)
            {
                if (!string.IsNullOrEmpty(_filterText))
                {
                    _filterText = "";
                    FilterModels();
                }
                else if (_app != null && MessageBox.Query(_app, "Exit Manage", "Are you sure you want to exit manage mode?", "Yes", "No") == 0)
                {
                    _app.RequestStop();
                }
                return;
            }

            if (key == Key.Backspace)
            {
                if (_filterText.Length > 0)
                {
                    _filterText = _filterText[..^1];
                    FilterModels();
                }
                return;
            }

            // Ctrl+Left / Ctrl+Right: previous / next server
            if (key == Key.CursorRight.WithCtrl || key == Key.CursorLeft.WithCtrl)
            {
                SwitchServer(key == Key.CursorRight.WithCtrl ? 1 : -1);
                return;
            }

            // Right arrow or Enter: model details
            if (key == Key.CursorRight || key == Key.Enter)
            {
                ShowExtendedInfo();
                return;
            }

            if (key == Key.F1)
            {
                ShowText("Help", HelpText, 84);
                return;
            }

            if (key == Key.F2)
            {
                ExecuteRename();
                return;
            }

            // Action shortcuts (Ctrl+key)
            if (key.IsCtrl && !key.IsAlt)
            {
                HandleActionShortcut(key.NoCtrl.NoShift.KeyCode);
                return;
            }

            // Letters, digits and name characters: filter
            if (!key.IsAlt && key.TryGetPrintableRune(out var rune) && rune.IsAscii)
            {
                var c = (char)rune.Value;
                if (char.IsLetterOrDigit(c) || c is ':' or '-' or '_' or '.' or '/' or '*')
                {
                    _filterText += c;
                    FilterModels();
                    return;
                }
            }

            key.Handled = false;
        }

        // Toggle selection on current model
        private void ToggleSelection()
        {
            var model = CurrentModel();
            if (model == null) return;
            model.IsSelected = !model.IsSelected;

            // Move down, like a file manager
            var index = _modelListView!.SelectedItem ?? 0;
            if (index + 1 < _filteredModels.Count)
                _modelListView.SelectedItem = index + 1;
            UpdateTopBar();
            _modelListView.SetNeedsDraw();
        }

        // Filter models based on filter text
        private void FilterModels()
        {
            var current = CurrentModel()?.Name;
            if (string.IsNullOrEmpty(_filterText))
            {
                _filteredModels = _allModels.ToList();
            }
            else
            {
                var pattern = System.Text.RegularExpressions.Regex.Escape(_filterText).Replace("\\*", ".*");
                _filteredModels = _allModels.Where(m =>
                    System.Text.RegularExpressions.Regex.IsMatch(m.Name, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                ).ToList();
            }
            _selectedModelName ??= current;
            ApplySortOrder();
        }

        // Handle action shortcuts (Ctrl+key)
        private void HandleActionShortcut(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.C: ExecuteCopy(); break;
                case KeyCode.M: ExecuteRename(); break;
                case KeyCode.R: ExecuteRun(); break;
                case KeyCode.S: ExecuteShow(); break;
                case KeyCode.D: ExecuteDelete(); break;
                case KeyCode.U: ExecuteUpdate(); break;
                case KeyCode.P: ExecutePull(); break;
                case KeyCode.L: ExecuteLoad(); break;
                case KeyCode.K: ExecuteUnload(); break;
                case KeyCode.X: ExecutePs(); break;
                case KeyCode.O: CycleSortOrder(); break;
                case KeyCode.T: ExecuteThemePicker(); break;
                case KeyCode.E: ExecuteSettings(); break;
                case KeyCode.W: ExecuteTweak(); break;
                case KeyCode.Q: _app?.RequestStop(); break;
            }
        }

        // Cycle through sort orders
        private void CycleSortOrder()
        {
            _selectedModelName = CurrentModel()?.Name;
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
        }

        // Apply current sort order to filtered models
        private void ApplySortOrder()
        {
            _filteredModels = _currentSortOrder switch
            {
                SortOrder.AlphabeticalDesc => _filteredModels.OrderByDescending(m => m.Name, StringComparer.Ordinal).ToList(),
                SortOrder.SizeAsc => _filteredModels.OrderBy(m => m.Size).ToList(),
                SortOrder.SizeDesc => _filteredModels.OrderByDescending(m => m.Size).ToList(),
                SortOrder.CreatedAsc => _filteredModels.OrderBy(m => m.ModifiedAt).ToList(),
                SortOrder.CreatedDesc => _filteredModels.OrderByDescending(m => m.ModifiedAt).ToList(),
                _ => _filteredModels.OrderBy(m => m.Name, StringComparer.Ordinal).ToList()
            };
            UpdateModelList();
        }

        // ------------------------------------------------------------------------------------------------
        // Dialog helpers
        // ------------------------------------------------------------------------------------------------

        private static readonly Rune NoHotKey = (Rune)0xFFFF;   // '_' in model names is not a hotkey marker

        private FormDialog NewDialog(string title, int width, int? height = null)
        {
            var dialog = new FormDialog
            {
                Title = title,
                HotKeySpecifier = NoHotKey,
                Width = Math.Min(width, Math.Max(20, ScreenWidth - 2))
            };
            if (height != null)
                dialog.Height = Math.Min(height.Value, Math.Max(8, (_app?.Screen.Height ?? 24) - 2));
            return dialog;
        }

        private static Label NewLabel(string text, int x, int y) =>
            new() { Text = text, X = x, Y = y, HotKeySpecifier = NoHotKey };

        private static TextField NewField(string text, int x, int y) =>
            new() { Text = text, X = x, Y = y, Width = Dim.Fill(1) };

        private static Button NewButton(string text) =>
            new() { Text = text, HotKeySpecifier = NoHotKey, ShadowStyle = ShadowStyles.None };

        /// <summary>Puts the selector's focus on its selected option (Enter accepts the focused option).</summary>
        private static void FocusSelected(OptionSelector selector)
        {
            try
            {
                selector.FocusedItem = selector.Value ?? 0;
            }
            catch (ArgumentOutOfRangeException)
            {
                // Options not created yet
            }
        }

        /// <summary>Runs a dialog; returns the index of the button pressed (null = Esc) and disposes it.</summary>
        private int? RunDialog(FormDialog dialog)
        {
            if (_app == null) return null;
            try
            {
                _app.Run(dialog);
                return dialog.Result;
            }
            finally
            {
                dialog.Dispose();
                _modelListView?.SetFocus();
            }
        }

        private void ShowError(string message)
        {
            if (_app != null) MessageBox.ErrorQuery(_app, "Error", message, "OK");
        }

        private void ShowInfo(string title, string message)
        {
            if (_app != null) MessageBox.Query(_app, title, message, "OK");
        }

        /// <summary>Read-only text in a scrollable dialog.</summary>
        private void ShowText(string title, string text, int width = 100)
        {
            var dialog = NewDialog(title, width, 24);
#pragma warning disable CS0618 // TextView is superseded by a separate package; it is enough for read-only text
            var textView = new TextView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                ReadOnly = true,
                WordWrap = false,
                Text = text
            };
#pragma warning restore CS0618
            dialog.Add(textView);
            dialog.AddButton(NewButton("Close"));
            textView.SetFocus();
            RunDialog(dialog);
        }

        /// <summary>Selected (checked) models, or the model under the cursor when none is checked.</summary>
        private List<ManageModelInfo> TargetModels(out bool isBatch)
        {
            var selected = _filteredModels.Where(m => m.IsSelected).ToList();
            isBatch = selected.Count > 0;
            if (isBatch) return selected;
            var current = CurrentModel();
            return current == null ? new List<ManageModelInfo>() : new List<ManageModelInfo> { current };
        }

        // ------------------------------------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------------------------------------

        // Action: Copy model(s)
        private void ExecuteCopy()
        {
            var selectedModels = TargetModels(out bool isBatchCopy);
            if (selectedModels.Count == 0) return;

            string title = isBatchCopy ? $"Copy {selectedModels.Count} models" : $"Copy {selectedModels[0].Name}";
            var dialog = NewDialog(title, 70);

            TextField? destNameField = null;
            int y = 0;
            if (!isBatchCopy)
            {
                dialog.Add(NewLabel("Destination name:", 0, y));
                destNameField = NewField(selectedModels[0].Name, 0, y + 1);
                dialog.Add(destNameField);
                y += 3;
            }

            dialog.Add(NewLabel(isBatchCopy ? "Remote server (required):" : "Remote server (empty for the same server):", 0, y));
            var serverField = NewField(_lastCopyDestinationServer ?? "", 0, y + 1);
            dialog.Add(serverField);
            y += 3;
            if (_destinationHistory.Count > 0)
            {
                dialog.Add(NewLabel("(Tab in the empty field cycles through recent servers)", 0, y - 1));
            }

            dialog.Add(NewLabel("Bandwidth throttle (empty = unlimited, e.g. 10MB):", 0, y));
            var throttleField = NewField("", 0, y + 1);
            dialog.Add(throttleField);

            // Tab in the empty server field cycles through the servers used in this session
            int historyIndex = -1;
            serverField.KeyDown += (_, key) =>
            {
                if (key == Key.Tab && string.IsNullOrEmpty(serverField.Text) && _destinationHistory.Count > 0)
                {
                    key.Handled = true;
                    historyIndex = (historyIndex + 1) % _destinationHistory.Count;
                    serverField.Text = _destinationHistory[historyIndex];
                    serverField.InsertionPoint = serverField.Text.Length;
                }
            };

            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Copy"));

            dialog.Validate = () =>
            {
                var destName = destNameField?.Text.Trim();
                var destServer = serverField.Text.Trim();

                if (!isBatchCopy && string.IsNullOrWhiteSpace(destName))
                {
                    ShowError("Destination name cannot be empty");
                    return false;
                }
                if (isBatchCopy && string.IsNullOrWhiteSpace(destServer))
                {
                    ShowError("Remote server is required for batch copy");
                    return false;
                }
                // A copy on the same server must not overwrite an existing model
                if (!isBatchCopy && string.IsNullOrEmpty(destServer) &&
                    _allModels.Any(m => m.Name.Equals(destName, StringComparison.OrdinalIgnoreCase) ||
                                        m.Name.Equals(destName + ":latest", StringComparison.OrdinalIgnoreCase)))
                {
                    ShowError($"Model '{destName}' already exists");
                    return false;
                }
                return true;
            };

            if (RunDialog(dialog) != 1) return;

            var name = destNameField?.Text.Trim();
            var server = serverField.Text.Trim();
            var throttle = throttleField.Text.Trim();
            bool isLocalCopy = string.IsNullOrEmpty(server);

            // Remember the destination server for this session
            if (!isLocalCopy)
            {
                _lastCopyDestinationServer = server;
                _destinationHistory.Remove(server);
                _destinationHistory.Insert(0, server);
                if (_destinationHistory.Count > 10)
                    _destinationHistory.RemoveAt(_destinationHistory.Count - 1);
            }

            RequestConsoleAction(token =>
            {
                if (!string.IsNullOrEmpty(throttle))
                    _program.BandwidthThrottling = throttle;

                string SourceOf(ManageModelInfo m) =>
                    string.IsNullOrEmpty(_destination) ? m.Name : _destination.TrimEnd('/') + "/" + m.Name;

                if (isBatchCopy)
                {
                    Console.WriteLine($"Copying {selectedModels.Count} models to {server}...\n");
                    int successCount = 0, failCount = 0;
                    foreach (var mdl in selectedModels)
                    {
                        try
                        {
                            Console.WriteLine($"--- Copying {mdl.Name} ({successCount + failCount + 1}/{selectedModels.Count}) ---");
                            _program.ActionCopy(SourceOf(mdl), server.TrimEnd('/') + "/" + mdl.Name, null, token);
                            successCount++;
                            Out.Success($"Successfully copied {mdl.Name}\n");
                        }
                        catch (Exception ex) when (IsCancellation(ex))
                        {
                            Console.WriteLine($"\nBatch copy cancelled: {successCount} succeeded, {failCount} failed");
                            throw;
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Out.Failure($"Failed to copy {mdl.Name}: {ex.Message}\n");
                        }
                    }
                    Console.WriteLine($"\nBatch copy completed: {successCount} succeeded, {failCount} failed");
                    return selectedModels[0].Name;
                }

                // Single copy. When manage shows a remote server, "same server" means that server.
                string destination = isLocalCopy
                    ? (string.IsNullOrEmpty(_destination) ? name! : _destination.TrimEnd('/') + "/" + name)
                    : server.TrimEnd('/') + "/" + name;
                _program.ActionCopy(SourceOf(selectedModels[0]), destination, null, token);
                return isLocalCopy ? name : selectedModels[0].Name;
            });
        }

        // Action: Rename model
        private void ExecuteRename()
        {
            var model = CurrentModel();
            if (model == null) return;

            var dialog = NewDialog($"Rename {model.Name}", 70);
            dialog.Add(NewLabel("New name:", 0, 0));
            var newNameField = NewField(model.Name, 0, 1);
            dialog.Add(newNameField);
            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Rename"));

            dialog.Validate = () =>
            {
                var candidate = newNameField.Text.Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    ShowError("New name cannot be empty");
                    return false;
                }
                if (candidate == model.Name)
                {
                    ShowError("New name must be different");
                    return false;
                }
                if (_allModels.Any(m => m.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                                        m.Name.Equals(candidate + ":latest", StringComparison.OrdinalIgnoreCase)))
                {
                    ShowError($"Model '{candidate}' already exists");
                    return false;
                }
                return true;
            };

            if (RunDialog(dialog) != 1) return;
            var newName = newNameField.Text.Trim();

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromMinutes(5)
                };

                // Rename is copy + delete
                var response = httpClient.PostAsync("/api/copy", new StringContent(
                    JsonSerializer.Serialize(new { source = model.Name, destination = newName }),
                    Encoding.UTF8, "application/json")).Result;
                response.EnsureSuccessStatusCode();

                var deleteMessage = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { name = model.Name }), Encoding.UTF8, "application/json")
                };
                response = httpClient.SendAsync(deleteMessage).Result;
                response.EnsureSuccessStatusCode();

                ReloadModels(newName.Contains(':') ? newName : newName + ":latest");
                SetStatus($"Renamed '{model.Name}' to '{newName}'");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to rename model: {ex.Message}");
            }
        }

        // Action: Run/chat with model
        private void ExecuteRun()
        {
            var model = CurrentModel();
            if (model == null) return;

            var dialog = NewDialog($"Run/Chat - {model.Name}", 76);
            const int fieldX = 28;

            dialog.Add(NewLabel("Format (--format):", 0, 0));
            var formatField = NewField("", fieldX, 0);
            dialog.Add(formatField);

            dialog.Add(NewLabel("KeepAlive (--keepalive):", 0, 2));
            var keepAliveField = NewField("", fieldX, 2);
            dialog.Add(keepAliveField);

            dialog.Add(NewLabel("Dimensions (--dimensions):", 0, 4));
            var dimensionsField = NewField("", fieldX, 4);
            dialog.Add(dimensionsField);

            dialog.Add(NewLabel("Think (--think):", 0, 6));
            var thinkOptions = new[] { "default", "true", "false", "high", "medium", "low" };
            var thinkSelector = new OptionSelector
            {
                X = 18,
                Y = 6,
                Orientation = Orientation.Horizontal,
                TabBehavior = TabBehavior.NoStop,
                Labels = thinkOptions,
                Value = 0
            };
            dialog.Add(thinkSelector);

            CheckBox Check(string text, int y, bool value = false)
            {
                var box = new CheckBox { Text = text, X = 0, Y = y, HotKeySpecifier = NoHotKey, Value = value ? CheckState.Checked : CheckState.UnChecked };
                dialog.Add(box);
                return box;
            }

            var noWordWrapCheck = Check("NoWordWrap (--no-wordwrap)", 8);
            var verboseCheck = Check("Verbose (--verbose)", 9);
            var hideThinkingCheck = Check("HideThinking (--hide-thinking)", 10);
            var insecureCheck = Check("Insecure (--insecure)", 11);
            var truncateCheck = Check("Truncate (--truncate)", 12, true);

            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Start Chat"));
            formatField.SetFocus();

            if (RunDialog(dialog) != 1) return;

            static bool On(CheckBox box) => box.Value == CheckState.Checked;
            var format = formatField.Text.Trim();
            var keepAlive = keepAliveField.Text.Trim();
            int? dimensions = int.TryParse(dimensionsField.Text.Trim(), out var dim) ? dim : null;
            var think = thinkSelector.Value is int ti && ti > 0 ? thinkOptions[ti] : "";

            var runArgs = new RunArgs
            {
                ModelName = model.Name,
                Destination = _destination ?? "",
                Format = format,
                KeepAlive = keepAlive,
                NoWordWrap = On(noWordWrapCheck),
                Verbose = On(verboseCheck),
                Dimensions = dimensions,
                HideThinking = On(hideThinkingCheck),
                Insecure = On(insecureCheck),
                Think = think,
                Truncate = On(truncateCheck)
            };

            RequestConsoleAction(_ =>
            {
                _program.Run(runArgs).GetAwaiter().GetResult();
                return model.Name;
            });
        }

        // Action: Show model info
        private void ExecuteShow()
        {
            var model = CurrentModel();
            if (model == null) return;

            var options = new[] { "License", "Modelfile", "Parameters", "System", "Template" };
            var dialog = NewDialog($"Show - {model.Name}", 50);
            dialog.Add(NewLabel("Information to display:", 0, 0));
            var selector = new OptionSelector { X = 1, Y = 1, TabBehavior = TabBehavior.NoStop, Labels = options, Value = 1 };
            dialog.Add(selector);
            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Show"));
            selector.SetFocus();
            FocusSelected(selector);

            if (RunDialog(dialog) != 1 || selector.Value is not int selectedIndex) return;
            var selectedOption = options[selectedIndex];

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                var response = httpClient.PostAsync("/api/show", new StringContent(
                    JsonSerializer.Serialize(new { name = model.Name, verbose = false }),
                    Encoding.UTF8, "application/json")).Result;
                response.EnsureSuccessStatusCode();

                using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                var property = selectedOption.ToLowerInvariant();
                var content = doc.RootElement.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String
                    ? element.GetString()
                    : null;
                if (string.IsNullOrEmpty(content))
                    content = $"(No {property})";

                ShowText($"{selectedOption} - {model.Name}", content);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to get model info: {ex.Message}");
            }
        }

        // Action: Delete model(s)
        private void ExecuteDelete()
        {
            var selectedModels = TargetModels(out bool isBatchDelete);
            if (selectedModels.Count == 0 || _app == null) return;

            string confirmMessage = isBatchDelete
                ? $"Are you sure you want to delete {selectedModels.Count} models?\n\n{string.Join("\n", selectedModels.Take(15).Select(m => m.Name))}{(selectedModels.Count > 15 ? "\n..." : "")}\n\nThis action cannot be undone."
                : $"Are you sure you want to delete '{selectedModels[0].Name}'?\n\nThis action cannot be undone.";
            string dialogTitle = isBatchDelete ? $"Confirm Delete ({selectedModels.Count} models)" : "Confirm Delete";

            // Enter presses the last button: keep Cancel there, so Enter never deletes
            if (MessageBox.ErrorQuery(_app, dialogTitle, confirmMessage, "Delete", "Cancel") != 0)
                return;

            int successCount = 0;
            int failCount = 0;
            string? lastError = null;
            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                foreach (var model in selectedModels)
                {
                    try
                    {
                        var requestMessage = new HttpRequestMessage(HttpMethod.Delete, "/api/delete")
                        {
                            Content = new StringContent(JsonSerializer.Serialize(new { name = model.Name }), Encoding.UTF8, "application/json")
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
            }
            catch (Exception ex)
            {
                ShowError($"Failed to delete model(s): {ex.Message}");
                return;
            }

            // Cursor on the model after the deleted one
            var index = _modelListView?.SelectedItem ?? 0;
            var deleted = selectedModels.Select(m => m.Name).ToHashSet();
            var next = _filteredModels.Skip(index).FirstOrDefault(m => !deleted.Contains(m.Name))
                       ?? _filteredModels.LastOrDefault(m => !deleted.Contains(m.Name));
            ReloadModels(next?.Name ?? "");

            if (failCount == 0)
                SetStatus(isBatchDelete ? $"{successCount} model(s) deleted" : $"{selectedModels[0].Name} deleted");
            else if (successCount == 0)
                ShowError($"Failed to delete model(s): {lastError}");
            else
                ShowInfo("Partial Success", $"{successCount} deleted, {failCount} failed\nLast error: {lastError}");
        }

        // Action: Update model(s)
        private void ExecuteUpdate()
        {
            var selectedModels = TargetModels(out bool isBatchUpdate);
            if (selectedModels.Count == 0 || _app == null) return;

            string confirmMessage = isBatchUpdate
                ? $"Update {selectedModels.Count} models to the latest versions?"
                : $"Update '{selectedModels[0].Name}' to the latest version?";
            if (MessageBox.Query(_app, "Confirm Update", confirmMessage, "Cancel", "Update") != 1)
                return;

            RequestConsoleAction(_ =>
            {
                Console.WriteLine($"\nUpdating {selectedModels.Count} model(s)...\n");
                foreach (var model in selectedModels)
                {
                    try
                    {
                        _program.ActionUpdate(model.Name, _destination ?? "");
                    }
                    catch (Exception ex)
                    {
                        Out.Failure($"Failed to update {model.Name}: {ex.Message}");
                    }
                }
                return selectedModels[0].Name;
            });
        }

        // Action: Pull new model
        private void ExecutePull()
        {
            var dialog = NewDialog("Pull Model", 70);
            dialog.Add(NewLabel("Model name (e.g. llama3, mistral:7b, hf.co/user/repo:Q4_K_M):", 0, 0));
            var modelNameField = NewField("", 0, 1);
            dialog.Add(modelNameField);
            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Pull"));

            string modelName = "";
            dialog.Validate = () =>
            {
                modelName = modelNameField.Text.Trim();
                if (string.IsNullOrWhiteSpace(modelName))
                {
                    ShowError("Model name cannot be empty");
                    return false;
                }

                // Tag defaults to latest (only after the last '/', so host:port prefixes do not count)
                if (!modelName[(modelName.LastIndexOf('/') + 1)..].Contains(':'))
                    modelName += ":latest";

                var error = ValidateRegistryModel(modelName);
                if (error != null)
                {
                    ShowError(error);
                    return false;
                }
                return true;
            };

            if (RunDialog(dialog) != 1) return;

            RequestConsoleAction(_ =>
            {
                Console.WriteLine($"\nPulling model '{modelName}'...\n");
                _program.ActionPull(modelName, _destination ?? "");
                return modelName;
            });
        }

        /// <summary>
        /// Checks that an Ollama library model exists before leaving the TUI. Models from other registries
        /// (hf.co/..., host/...) are not checked. Returns an error message, or null.
        /// </summary>
        private static string? ValidateRegistryModel(string modelName)
        {
            var slash = modelName.IndexOf('/');
            if (slash > 0 && (modelName[..slash].Contains('.') || modelName[..slash].Contains(':')))
                return null;

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                var colon = modelName.LastIndexOf(':');
                var model = modelName[..colon];
                var tag = modelName[(colon + 1)..];
                var repository = model.Contains('/') ? model : $"library/{model}";

                var checkResponse = httpClient.GetAsync($"https://registry.ollama.ai/v2/{repository}/manifests/{tag}").Result;
                var responseContent = checkResponse.Content.ReadAsStringAsync().Result;

                if (responseContent.Contains("MANIFEST_UNKNOWN") || checkResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                    return $"Model '{modelName}' not found on Ollama.\n\nPlease check the model name and try again.";
                if (!checkResponse.IsSuccessStatusCode)
                    return $"Failed to validate model (HTTP {(int)checkResponse.StatusCode}).\n\nPlease check your internet connection.";
                if (!responseContent.Contains("schemaVersion"))
                    return "Invalid response from Ollama registry.\n\nPlease try again later.";
                return null;
            }
            catch (Exception ex)
            {
                return $"Failed to validate model: {ex.Message}\n\nPlease check your internet connection.";
            }
        }

        // Action: Load model into memory (in the background; the list stays usable)
        private void ExecuteLoad()
        {
            var model = CurrentModel();
            var app = _app;
            if (model == null || app == null) return;

            var serverUrl = ServerUrl;
            SetStatus($"Loading {model.Name}...");
            Task.Run(() =>
            {
                string? error = null;
                try
                {
                    using var httpClient = new HttpClient
                    {
                        BaseAddress = new Uri(serverUrl),
                        Timeout = TimeSpan.FromMinutes(5)
                    };

                    // Chat request without messages: loads the model without unloading others
                    var preloadRequest = new { model = model.Name, messages = Array.Empty<object>(), keep_alive = "5m", stream = false };
                    var response = httpClient.PostAsync("/api/chat", new StringContent(
                        JsonSerializer.Serialize(preloadRequest), Encoding.UTF8, "application/json")).Result;
                    response.EnsureSuccessStatusCode();
                    response.Content.ReadAsStringAsync().Wait();
                }
                catch (Exception ex)
                {
                    error = ex.InnerException?.Message ?? ex.Message;
                }

                try
                {
                    app.Invoke(() =>
                    {
                        if (_app != app) return;   // the session has ended meanwhile
                        FetchRunningStatus();
                        _modelListView?.SetNeedsDraw();
                        if (error != null)
                            ShowError($"Failed to load {model.Name}: {error}");
                        else
                            SetStatus($"{model.Name} loaded into memory");
                    });
                }
                catch
                {
                    // The session ended (and the application was disposed) while the model was loading
                }
            });
        }

        // Action: Unload model from memory
        private void ExecuteUnload()
        {
            var model = CurrentModel();
            if (model == null) return;

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(30)
                };

                // keep_alive = 0 unloads the model
                var response = httpClient.PostAsync("/api/generate", new StringContent(
                    JsonSerializer.Serialize(new { model = model.Name, keep_alive = 0 }),
                    Encoding.UTF8, "application/json")).Result;
                response.EnsureSuccessStatusCode();

                FetchRunningStatus();
                _modelListView?.SetNeedsDraw();
                SetStatus($"{model.Name} unloaded from memory");
            }
            catch (Exception ex)
            {
                ShowError($"Failed to unload model: {ex.Message}");
            }
        }

        // Action: Show process status
        private void ExecutePs()
        {
            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ServerUrl),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var response = httpClient.GetAsync("/api/ps").Result;
                if (!response.IsSuccessStatusCode)
                {
                    ShowError($"Failed to get process status (HTTP {response.StatusCode})");
                    return;
                }

                var status = JsonSerializer.Deserialize<ProcessStatusResponse>(response.Content.ReadAsStringAsync().Result);
                if (status?.Models == null || status.Models.Count == 0)
                {
                    ShowInfo("Process Status", "No models currently loaded in memory");
                    return;
                }

                // Same format as the CLI
                var lines = new List<string>
                {
                    $"{"NAME",-30} {"ID",-15} {"SIZE",-25} {"VRAM USAGE",-15} {"CONTEXT",-10} {"UNTIL",-30}",
                    new string('-', 130)
                };

                foreach (var model in status.Models)
                {
                    var name = TruncateString(model.Name, 30);
                    var id = GetShortDigest(model.Digest);
                    var size = FormatModelSize(model.Size, model.Details?.ParameterSize);

                    string vramUsage = model.SizeVram < model.Size && model.Size > 0
                        ? $"{FormatBytes(model.SizeVram)} ({(double)model.SizeVram / model.Size * 100:F0}%)"
                        : FormatBytes(model.SizeVram);

                    var context = model.ContextLength > 0 ? model.ContextLength.ToString() : "N/A";
                    lines.Add($"{name,-30} {id,-15} {size,-25} {vramUsage,-15} {context,-10} {FormatUntil(model.ExpiresAt),-30}");
                }

                ShowText("Process Status", string.Join("\n", lines), 140);
            }
            catch (Exception ex)
            {
                ShowError($"Failed to get process status: {ex.Message}");
            }
        }

        // Show extended information about the selected model
        private void ShowExtendedInfo()
        {
            var model = CurrentModel();
            if (model == null) return;

            if (!model.ExtendedInfoLoaded)
            {
                FetchExtendedInfo(model);
            }

            var lines = new List<string>
            {
                $"Model: {model.Name}",
                $"ID: {model.ShortId}",
                $"Size: {model.SizeFormatted}",
                $"Modified: {model.ModifiedFormatted}",
                ""
            };

            if (!string.IsNullOrEmpty(model.Family))
                lines.Add($"Family: {model.Family}");
            if (!string.IsNullOrEmpty(model.ParameterSize))
                lines.Add($"Parameters: {model.ParameterSize}");
            if (!string.IsNullOrEmpty(model.Quantization))
                lines.Add($"Quantization: {model.Quantization}");
            if (model.IsLoaded)
                lines.Add("Status: LOADED IN MEMORY");
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
                if (model.XOllamaSettings is { Count: > 0 } xollama)
                {
                    lines.Add("");
                    lines.Add("--- xOllama settings (Ctrl+W to change) ---");
                    lines.AddRange(XOllamaTweak.Describe(xollama));
                }
            }
            else
            {
                lines.Add("(Extended info could not be loaded)");
            }

            ShowText($"Model Info - {model.Name}", string.Join("\n", lines), 76);
        }

        private bool IsXOllamaServer => OllamaServer.GetFlavor(ServerUrl) == ServerFlavor.XOllama;

        // Action: xOllama settings of the model(s), and of the server when it runs on this machine, with
        // `xollama tweak` on the console
        private void ExecuteTweak()
        {
            if (_app == null) return;
            var models = TargetModels(out bool isBatch);
            var url = ServerUrl;
            var local = XOllamaTweak.IsOnThisMachine(url);
            if (models.Count == 0 && !local) return;

            if (!IsXOllamaServer)
            {
                ShowInfo("Tweak", $"Tweak changes xOllama's own settings,\nand {new Uri(url).Authority} is not an xOllama server.");
                return;
            }
            var cli = OllamaServer.XOllamaCli;
            if (cli == null)
            {
                ShowError("Tweak runs the xollama CLI (xollama tweak), which is not on PATH.\n" +
                          "Install xOllama on this machine, or set OSYNC_XOLLAMA_CLI to its path.");
                return;
            }

            // The model options (none without a model), then the server's own: xOllama takes changes to those
            // only from its own machine
            var scopes = (models.Count > 0 ? XOllamaTweak.Scopes : Array.Empty<XOllamaTweak.Scope>())
                .Concat(local ? XOllamaTweak.ServerScopes : Array.Empty<XOllamaTweak.Scope>())
                .ToArray();
            var clearScope = models.Count > 0 ? XOllamaTweak.ClearScope : -1;

            var title = models.Count == 0 ? $"Tweak - {new Uri(url).Authority}"
                : isBatch ? $"Tweak - {models.Count} models" : $"Tweak - {models[0].Name}";
            var dialog = NewDialog(title, 82);
            // Rows left for the current settings: the options, the flags, the buttons and the borders take the
            // rest; a block (heading, rows, blank line) is left out when not even one row of it fits
            var room = _app.Screen.Height - scopes.Length - 8;
            int y = 0;
            if (models.Count > 0 && !isBatch && room >= 3)
            {
                FetchExtendedInfo(models[0]);
                var current = models[0].XOllamaSettings is { Count: > 0 } rows
                    ? XOllamaTweak.Describe(rows, 40).ToList()
                    : new List<string> { local ? "none: the server's defaults and environment decide" : "none: the server decides" };
                y = AddSettingRows(dialog, "xOllama settings now:", current, Math.Min(6, room - 3), y,
                    "model details show them all");
            }
            if (local && room - y >= 3 && XOllamaTweak.FetchServerDefaults(url) is { Count: > 0 } defaults)
                y = AddSettingRows(dialog, "Server defaults (for what a model leaves unset):",
                    XOllamaTweak.Describe(defaults, 40).ToList(), Math.Min(4, room - y - 3), y, "tweak show server lists them all");

            dialog.Add(NewLabel("Settings to change:", 0, y++));
            var selector = new OptionSelector
            {
                X = 1,
                Y = y,
                TabBehavior = TabBehavior.NoStop,
                HotKeySpecifier = NoHotKey,
                Labels = scopes.Select(s => s.Label).ToArray(),
                Value = 0
            };
            dialog.Add(selector);
            y += scopes.Length + 1;
            dialog.Add(NewLabel("Flags (optional; a flag with a value, --kv-v=q8_0, is set without asking):", 0, y++));
            var flagsField = NewField("", 0, y);
            dialog.Add(flagsField);
            dialog.Validate = () =>
            {
                if (string.IsNullOrWhiteSpace(flagsField.Text) || selector.Value is not int chosen) return true;
                if (chosen == clearScope)
                {
                    ShowError("Removing the settings cannot be combined with flags that set one.");
                    return false;
                }
                if (XOllamaTweak.IsShowScope(scopes[chosen]))
                {
                    ShowError("Showing the settings takes no flags: leave the field empty.");
                    return false;
                }
                if (XOllamaTweak.SplitFlags(flagsField.Text) == null)
                {
                    ShowError("A quote in the flags is not closed.");
                    return false;
                }
                return true;
            };
            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Tweak"));
            selector.SetFocus();
            FocusSelected(selector);

            if (RunDialog(dialog) != 1 || selector.Value is not int index) return;
            var scope = scopes[index];
            var extra = flagsField.Text.Trim();

            if (index == clearScope)
            {
                var what = isBatch ? $"{models.Count} models" : $"'{models[0].Name}'";
                // Enter presses the last button: keep Cancel there
                if (MessageBox.ErrorQuery(_app, "Remove xOllama settings",
                        $"Remove the xOllama settings of {what}?\nThe server's defaults and environment then decide every setting.",
                        "Remove", "Cancel") != 0)
                    return;
            }

            RequestConsoleAction(token =>
            {
                if (!scope.PerModel)
                {
                    Out.StatusLine($"\nxollama tweak {scope.Command} on {url}");
                    var code = XOllamaTweak.Run(cli, url, XOllamaTweak.Arguments(scope, null, extra));
                    if (code != 0)
                        Out.Failure($"xollama tweak {scope.Command} exited with code {code}");
                    return null;
                }
                foreach (var model in models)
                {
                    if (token.IsCancellationRequested) break;
                    Out.StatusLine($"\nxollama tweak {scope.Command} '{model.Name}' on {url}");
                    var code = XOllamaTweak.Run(cli, url, XOllamaTweak.Arguments(scope, model.Name, extra));
                    if (code != 0)
                        Out.Failure($"xollama tweak of '{model.Name}' exited with code {code}");
                }
                return models[0].Name;
            });
        }

        /// <summary>A heading and up to <paramref name="shown"/> setting rows of the tweak dialog; returns the next row.</summary>
        private static int AddSettingRows(Dialog dialog, string heading, List<string> lines, int shown, int y, string more)
        {
            dialog.Add(NewLabel(heading, 0, y++));
            foreach (var line in lines.Take(shown))
                dialog.Add(NewLabel("  " + line, 0, y++));
            if (lines.Count > shown)
                dialog.Add(NewLabel($"  ... and {lines.Count - shown} more ({more})", 0, y++));
            return y + 1;
        }

        // Action: choose the theme (live preview; the choice is saved in the settings file)
        private void ExecuteThemePicker()
        {
            var original = _theme;
            var names = new ObservableCollection<string>(Themes.All.Select(t => t.Name));
            var dialog = NewDialog("Theme", 40);

            // The list scrolls when the terminal is not tall enough for every theme (dialog border, button bar
            // and the optional depth note take the rest)
            var screenHeight = _app?.Screen.Height ?? 24;
            var noteRows = _depth != ColorDepth.TrueColor ? 2 : 0;
            var listHeight = Math.Clamp(screenHeight - 8 - noteRows, 3, names.Count);
            var list = new ListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = listHeight,
                KeystrokeNavigator = null
            };
            list.SetSource(names);
            list.SelectedItem = Themes.IndexOf(_theme);
            // Bring the current theme into view once the list has its size
            list.FrameChanged += (_, _) => list.EnsureSelectedItemVisible();
            list.ValueChanged += (_, e) =>
            {
                if (e.NewValue is int i && i >= 0 && i < Themes.All.Count)
                {
                    SetTheme(Themes.All[i]);
                    dialog.SetNeedsDraw();
                }
            };
            dialog.Add(list);

            if (_depth != ColorDepth.TrueColor)
            {
                dialog.Add(NewLabel($"Drawn with {ColorSupport.DisplayName(_depth)}", 0, listHeight + 1));
            }

            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Apply"));
            list.SetFocus();

            if (RunDialog(dialog) != 1)
            {
                SetTheme(original);
                return;
            }

            try
            {
                var settings = OsyncSettings.Current;
                settings.Manage.Theme = _theme.Name;
                settings.SaveAsCurrent();
                SetStatus($"Theme: {_theme.Name}");
            }
            catch (Exception ex)
            {
                SetStatus($"Theme: {_theme.Name} (not saved: {ex.Message})");
            }
        }

        // Action: settings (local server, colors), saved in the settings file
        private void ExecuteSettings()
        {
            var settings = OsyncSettings.Current;
            var dialog = NewDialog("Settings", 78);

            int y = 0;
            dialog.Add(NewLabel("Local server", 0, y++));
            dialog.Add(NewLabel("Type:", 1, y));
            var flavorOptions = new[] { "Auto-detect", "Ollama", "xOllama" };
            var flavorSelector = new OptionSelector
            {
                X = 12,
                Y = y,
                Orientation = Orientation.Horizontal,
                TabBehavior = TabBehavior.NoStop,
                Labels = flavorOptions,
                Value = settings.ConfiguredFlavor switch { ServerFlavor.Ollama => 1, ServerFlavor.XOllama => 2, _ => 0 }
            };
            dialog.Add(flavorSelector);
            y += 2;

            dialog.Add(NewLabel("Host / IP:", 1, y));
            var hostField = NewField(settings.Server.Host ?? "", 12, y);
            dialog.Add(hostField);
            y += 2;

            dialog.Add(NewLabel("Port:", 1, y));
            var portField = new TextField { X = 12, Y = y, Width = 8, Text = settings.Server.Port?.ToString() ?? "" };
            dialog.Add(portField);
            dialog.Add(NewLabel($"empty = {OllamaServer.OllamaDefaultPort} (Ollama) / {OllamaServer.XOllamaDefaultPort} (xOllama)", 22, y));
            y += 2;

            var testButton = NewButton("Test connection");
            testButton.X = 12;
            testButton.Y = y;
            var testResult = NewLabel("", 32, y);
            testResult.Width = Dim.Fill(1);
            dialog.Add(testButton, testResult);
            y += 2;

            var environmentVariable = OllamaServer.EnvironmentServerVariable(Environment.GetEnvironmentVariable);
            CheckBox? ignoreEnvironmentCheck = null;
            if (environmentVariable != null)
            {
                ignoreEnvironmentCheck = new CheckBox
                {
                    Text = $"Use this server even though {environmentVariable} is set",
                    X = 1,
                    Y = y++,
                    HotKeySpecifier = NoHotKey,
                    Value = settings.Server.IgnoreEnvironment == true ? CheckState.Checked : CheckState.UnChecked
                };
                dialog.Add(ignoreEnvironmentCheck);
                y++;
            }
            if (settings.Server.Both == true)
            {
                dialog.Add(NewLabel("Ollama and xOllama run side by side: the type is the default server", 1, y++));
                dialog.Add(NewLabel("(both servers and their aliases: osync setup server).", 1, y++));
                y++;
            }

            dialog.Add(NewLabel("Colors", 0, y++));
            var colorOptions = new[] { "Auto", "True color", "256", "16", "None" };
            var colorValues = new[] { "auto", "truecolor", "256", "16", "none" };
            var currentColor = ColorSupport.Parse(settings.ColorMode) switch
            {
                ColorDepth.TrueColor => 1,
                ColorDepth.Colors256 => 2,
                ColorDepth.Standard16 => 3,
                ColorDepth.None => 4,
                _ => 0
            };
            dialog.Add(NewLabel("Mode:", 1, y));
            var colorSelector = new OptionSelector
            {
                X = 12,
                Y = y,
                Orientation = Orientation.Horizontal,
                TabBehavior = TabBehavior.NoStop,
                Labels = colorOptions,
                Value = currentColor
            };
            dialog.Add(colorSelector);
            y += 2;
            dialog.Add(NewLabel($"Now: {ColorSupport.DisplayName(ColorSupport.Current)} ({ColorSupport.Reason})", 1, y++));
            dialog.Add(NewLabel($"File: {OsyncSettings.FilePath}", 1, y));

            string host = "";
            int? port = null;
            ServerFlavor? flavor = null;

            bool ReadServerFields()
            {
                host = hostField.Text.Trim();
                if (host.Length > 0 && Uri.CheckHostName(host) == UriHostNameType.Unknown)
                {
                    ShowError($"'{host}' is not a valid host name or IP address");
                    return false;
                }
                var portText = portField.Text.Trim();
                port = null;
                if (portText.Length > 0)
                {
                    if (!int.TryParse(portText, out var p) || p is < 1 or > 65535)
                    {
                        ShowError("Port must be a number between 1 and 65535");
                        return false;
                    }
                    port = p;
                }
                flavor = flavorSelector.Value switch { 1 => ServerFlavor.Ollama, 2 => ServerFlavor.XOllama, _ => null };
                return true;
            }

            string UrlFromFields()
            {
                var effectivePort = port ?? (flavor == ServerFlavor.XOllama ? OllamaServer.XOllamaDefaultPort : OllamaServer.OllamaDefaultPort);
                var effectiveHost = host.Length == 0 ? "localhost" : host;
                return OllamaServer.ToClientUrl($"{effectiveHost}:{effectivePort}", effectivePort);
            }

            testButton.Accepting += (_, e) =>
            {
                e.Handled = true;   // do not close the dialog
                if (!ReadServerFields()) return;
                var url = UrlFromFields();
                testResult.Text = $"Testing {url}...";
                var probe = ServerSetup.ProbeServer(url);
                testResult.Text = probe.Reachable
                    ? $"OK: {OllamaServer.DisplayName(probe.Flavor)} {probe.Version}"
                    : $"No server answers at {new Uri(url).Authority}";
            };

            dialog.AddButton(NewButton("Cancel"));
            dialog.AddButton(NewButton("Save"));
            dialog.Validate = ReadServerFields;
            flavorSelector.SetFocus();
            FocusSelected(flavorSelector);
            FocusSelected(colorSelector);

            if (RunDialog(dialog) != 1) return;

            var previousUrl = ServerUrl;
            var previousColorMode = settings.ColorMode;
            var effectiveFlavorPort = flavor == ServerFlavor.XOllama ? OllamaServer.XOllamaDefaultPort : OllamaServer.OllamaDefaultPort;
            settings.Server.Flavor = flavor switch { ServerFlavor.Ollama => "ollama", ServerFlavor.XOllama => "xollama", _ => "auto" };
            // Both servers side by side: the type chooses the default one; "auto" ends the side-by-side setup
            if (flavor == null) settings.Server.Both = null;
            settings.Server.Host = host.Length == 0 ? null : host;
            settings.Server.Port = port == effectiveFlavorPort ? null : port;
            settings.ColorMode = colorValues[colorSelector.Value ?? 0];
            if (ignoreEnvironmentCheck != null)
                settings.Server.IgnoreEnvironment = ignoreEnvironmentCheck.Value == CheckState.Checked && flavor != null ? true : null;

            try
            {
                settings.SaveAsCurrent();
            }
            catch (Exception ex)
            {
                ShowError($"Cannot save {OsyncSettings.FilePath}: {ex.Message}");
                return;
            }

            OllamaServer.ResetLocal();
            ColorSupport.Reset();
            var shownName = _targets[_targetIndex].Name;
            BuildTargets();
            _targetIndex = Math.Max(0, _targets.FindIndex(t => t.Name == shownName));
            _destination = _targets[_targetIndex].Url;

            if (!string.Equals(previousColorMode, settings.ColorMode, StringComparison.OrdinalIgnoreCase))
            {
                // The color depth is fixed when Terminal.Gui starts: restart the view
                _selectedModelName = CurrentModel()?.Name;
                _restartRequested = true;
                _app?.RequestStop();
                return;
            }

            if (string.IsNullOrEmpty(_destination) && !string.Equals(previousUrl, ServerUrl, StringComparison.OrdinalIgnoreCase))
            {
                ReloadModels();
                SetStatus($"Server: {_serverLabel}");
            }
            else
            {
                SetStatus(string.IsNullOrEmpty(_destination) ? "Settings saved" : $"Settings saved (this view shows {new Uri(_destination).Authority})");
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Helpers for ps
        // ------------------------------------------------------------------------------------------------

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str) || str.Length <= maxLength)
                return str ?? "";

            return str.Substring(0, maxLength - 3) + "...";
        }

        private static string GetShortDigest(string digest)
        {
            if (string.IsNullOrEmpty(digest))
                return "N/A";

            return digest.Length >= 12 ? digest.Substring(0, 12) : digest;
        }

        private static string FormatModelSize(long sizeBytes, string? parameterSize)
        {
            var diskSize = FormatBytes(sizeBytes);
            return string.IsNullOrEmpty(parameterSize) ? diskSize : $"{diskSize} ({parameterSize})";
        }

        private static string FormatBytes(long bytes)
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

        private static string FormatUntil(DateTime expiresAt)
        {
            var timeSpan = expiresAt - DateTime.Now;

            if (timeSpan.TotalMinutes < 1)
                return "Less than a minute";
            if (timeSpan.TotalMinutes < 2)
                return "About a minute from now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes from now";
            if (timeSpan.TotalHours < 2)
                return "About an hour from now";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours from now";
            return $"{(int)timeSpan.TotalDays} days from now";
        }

        // ------------------------------------------------------------------------------------------------
        // Views
        // ------------------------------------------------------------------------------------------------

        /// <summary>Model rows with one color per column.</summary>
        private sealed class ModelListSource : IListDataSource
        {
            private readonly ManageUI _owner;

            public ModelListSource(ManageUI owner) => _owner = owner;

            public event NotifyCollectionChangedEventHandler? CollectionChanged;
            public int Count => _owner._filteredModels.Count;
            public int MaxItemLength => 1;   // rows always fit the width: no horizontal scrolling
            public bool SuspendCollectionChangedEvent { get; set; }
            public bool IsMarked(int item) => false;
            public void SetMark(int item, bool value) { }
            public IList ToList() => _owner._filteredModels;
            public void Dispose() { }

            public void Reset()
            {
                if (!SuspendCollectionChangedEvent)
                    CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }

            public void Render(ListView listView, bool selected, int item, int col, int row, int width, int viewportX = 0)
            {
                var o = _owner;
                var t = o._drawTheme;
                if (item < 0 || item >= o._filteredModels.Count) return;
                var model = o._filteredModels[item];

                var bg = selected ? t.SelectionBackground : (item % 2 == 0 ? t.Background : t.AltBackground);
                Rgb Fg(Rgb color) => selected ? t.SelectionText : color;

                var nameWidth = o.NameWidth(width);
                var quant = string.IsNullOrEmpty(model.Quantization) ? "unknown" : model.Quantization;
                var id = model.ShortId.Length > 12 ? model.ShortId[..12] : model.ShortId;

                var segments = new (string Text, Rgb Fg, TextStyle Style)[]
                {
                    (model.IsSelected ? "[X] " : "[ ] ", Fg(model.IsSelected ? t.Checked : t.Muted), model.IsSelected ? TextStyle.Bold : TextStyle.None),
                    (model.IsLoaded ? LoadedMarker + " " : "  ", Fg(t.Loaded), TextStyle.None),
                    (Fit(model.Name, nameWidth, left: true) + " ", Fg(t.Text), selected ? TextStyle.Bold : TextStyle.None),
                    (Fit(model.SizeFormatted, o._sizeColumnWidth) + " ", Fg(t.Size), TextStyle.None),
                    (Fit(model.ParameterSize, o._paramsColumnWidth) + " ", Fg(t.Params), TextStyle.None),
                    (Fit(quant, o._quantColumnWidth) + " ", Fg(string.IsNullOrEmpty(model.Quantization) ? t.Muted : t.Quant), TextStyle.None),
                    (Fit(model.Family, o._familyColumnWidth) + " ", Fg(t.Family), TextStyle.None),
                    (Fit(model.ModifiedFormatted, o._modifiedColumnWidth) + " ", Fg(t.Muted), TextStyle.None),
                    (Fit(id, o._idColumnWidth), Fg(t.Id), TextStyle.None)
                };

                listView.Move(col, row);
                int remaining = width;
                foreach (var (text, fg, style) in segments)
                {
                    if (remaining <= 0) break;
                    var part = text.Length > remaining ? text[..remaining] : text;
                    listView.SetAttribute(A(fg, bg, style));
                    listView.AddStr(part);
                    remaining -= part.Length;
                }
                if (remaining > 0)
                {
                    listView.SetAttribute(A(t.Text, bg));
                    listView.AddStr(new string(' ', remaining));
                }

                // Rows below the last model are drawn with the current attribute: leave the normal one
                listView.SetAttribute(A(t.Text, t.Background));
            }
        }

        /// <summary>A one-line bar of colored text segments, left- and right-aligned.</summary>
        private sealed class SegmentBar : View
        {
            public readonly record struct Segment(string Text, TgColor Fg, bool Bold = false);

            private IReadOnlyList<Segment> _left = Array.Empty<Segment>();
            private IReadOnlyList<Segment> _right = Array.Empty<Segment>();
            private TgColor _background = new(0, 0, 0);

            public SegmentBar()
            {
                CanFocus = false;
            }

            public void Set(IReadOnlyList<Segment> left, IReadOnlyList<Segment> right, TgColor background)
            {
                _left = left;
                _right = right;
                _background = background;
                SetNeedsDraw();
            }

            protected override bool OnDrawingContent(DrawContext? context)
            {
                int width = Viewport.Width;
                Move(0, 0);
                int used = Draw(_left, width);

                int rightLength = _right.Sum(s => s.Text.Length);
                int gap = width - used - rightLength;
                if (gap >= 1)
                {
                    Pad(gap);
                    Draw(_right, rightLength);
                }
                else
                {
                    Pad(width - used);
                }
                return true;
            }

            private int Draw(IReadOnlyList<Segment> segments, int width)
            {
                int used = 0;
                foreach (var segment in segments)
                {
                    if (used >= width) break;
                    var text = segment.Text.Length > width - used ? segment.Text[..(width - used)] : segment.Text;
                    SetAttribute(new TgAttribute(segment.Fg, _background, segment.Bold ? TextStyle.Bold : TextStyle.None));
                    AddStr(text);
                    used += text.Length;
                }
                return used;
            }

            private void Pad(int count)
            {
                if (count <= 0) return;
                SetAttribute(new TgAttribute(_background, _background));
                AddStr(new string(' ', count));
            }
        }

        /// <summary>Dialog whose accept (default button, Enter in a field) can be vetoed by <see cref="Validate"/>.</summary>
        private sealed class FormDialog : Dialog
        {
            public Func<bool>? Validate { get; set; }

            public FormDialog()
            {
                ShadowStyle = ShadowStyles.None;
            }

            protected override bool OnAccepting(CommandEventArgs args)
            {
                View? source = null;
                args.Context?.Source?.TryGetTarget(out source);
                bool isOtherButton = source is Button b && Buttons.Contains(b) && b != Buttons[^1];

                if (!isOtherButton && Validate != null && !Validate())
                {
                    return true;   // keep the dialog open
                }
                return base.OnAccepting(args);
            }
        }
    }
}
