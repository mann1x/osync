using System.Text.Json;
using System.Text.Json.Serialization;

namespace osync
{
    /// <summary>
    /// User preferences, stored as JSON in the per-OS configuration directory:
    ///   Windows  %APPDATA%\osync\settings.json
    ///   macOS    ~/Library/Application Support/osync/settings.json
    ///   Linux    $XDG_CONFIG_HOME/osync/settings.json (default ~/.config/osync/settings.json)
    /// OSYNC_CONFIG_DIR overrides the directory. Environment variables (XOLLAMA_HOST, OLLAMA_HOST,
    /// OSYNC_COLOR_MODE, ...) take precedence over the file; command-line options over both.
    /// </summary>
    internal sealed class OsyncSettings
    {
        public const string FileName = "settings.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static readonly object CurrentLock = new();
        private static OsyncSettings? _current;

        /// <summary>Schema version of the file, for future migrations.</summary>
        public int Version { get; set; } = 1;

        /// <summary>The local server: Ollama or xOllama, and where it listens.</summary>
        public ServerSettings Server { get; set; } = new();

        /// <summary>auto (detect), truecolor, 256, 16 or none.</summary>
        public string ColorMode { get; set; } = "auto";

        public ManageSettings Manage { get; set; } = new();

        public ShellSettings Shell { get; set; } = new();

        /// <summary>
        /// Server aliases: name → server URL (e.g. "gpu" → "http://192.168.1.10:11434"). An alias can be used wherever
        /// a server is expected: <c>-d gpu</c>, <c>osync cp model gpu/</c>, <c>osync cp gpu/model local-copy</c>.
        /// </summary>
        public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public sealed class ServerSettings
        {
            /// <summary>auto (detect), ollama or xollama.</summary>
            public string Flavor { get; set; } = "auto";

            /// <summary>Host name or IP of the local server; null = localhost.</summary>
            public string? Host { get; set; }

            /// <summary>Port; null = the flavor's default (11434 Ollama, 22434 xOllama).</summary>
            public int? Port { get; set; }

            /// <summary>
            /// Ollama and xOllama run side by side on the host: <see cref="Flavor"/> is the default one (the local
            /// server of every command), the other is reached through its alias ("ollama" / "xollama").
            /// </summary>
            public bool? Both { get; set; }
        }

        public sealed class ManageSettings
        {
            /// <summary>Name of the last used manage theme.</summary>
            public string? Theme { get; set; }

            /// <summary>Initial sort order: name+, name-, size+, size-, created+, created- (default name+).</summary>
            public string? Sort { get; set; }
        }

        public sealed class ShellSettings
        {
            /// <summary>Theme of the command output in the shell (same names as manage, or "plain"); null = default.</summary>
            public string? Theme { get; set; }
        }

        /// <summary>Directory holding settings.json.</summary>
        public static string ConfigDirectory
        {
            get
            {
                var overrideDir = Environment.GetEnvironmentVariable("OSYNC_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir))
                    return overrideDir.Trim();

                if (OperatingSystem.IsWindows())
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "osync");

                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (OperatingSystem.IsMacOS())
                    return Path.Combine(home, "Library", "Application Support", "osync");

                var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
                return Path.Combine(string.IsNullOrWhiteSpace(xdg) ? Path.Combine(home, ".config") : xdg, "osync");
            }
        }

        public static string FilePath => Path.Combine(ConfigDirectory, FileName);

        /// <summary>Settings of this run, loaded once from <see cref="FilePath"/>.</summary>
        public static OsyncSettings Current
        {
            get
            {
                lock (CurrentLock)
                {
                    return _current ??= Load(FilePath);
                }
            }
        }

        /// <summary>
        /// Reads settings from <paramref name="path"/>. A missing file gives the defaults; an unreadable or
        /// invalid file gives the defaults and a warning on stderr (the file is left untouched).
        /// </summary>
        public static OsyncSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new OsyncSettings();
                var settings = JsonSerializer.Deserialize<OsyncSettings>(File.ReadAllText(path), JsonOptions) ?? new OsyncSettings();
                settings.Server ??= new ServerSettings();
                settings.Manage ??= new ManageSettings();
                settings.Shell ??= new ShellSettings();
                settings.Aliases = new Dictionary<string, string>(settings.Aliases ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                settings.ColorMode ??= "auto";
                return settings;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Warning: ignoring settings file {path}: {ex.Message}");
                return new OsyncSettings();
            }
        }

        /// <summary>Writes the settings atomically (temporary file, then replace).</summary>
        public void Save(string? path = null)
        {
            path ??= FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, overwrite: true);
        }

        /// <summary>Saves and makes these settings the current ones for the rest of the run.</summary>
        public void SaveAsCurrent()
        {
            Save();
            lock (CurrentLock)
            {
                _current = this;
            }
        }

        /// <summary>Configured server flavor, or null when it should be detected.</summary>
        [JsonIgnore]
        public ServerFlavor? ConfiguredFlavor => Server.Flavor?.Trim().ToLowerInvariant() switch
        {
            "ollama" => ServerFlavor.Ollama,
            "xollama" => ServerFlavor.XOllama,
            _ => null
        };

        /// <summary>
        /// URL of the configured local server, or null when neither a host, a port nor a flavor is configured.
        /// The port defaults to the flavor's default port.
        /// </summary>
        [JsonIgnore]
        public string? ConfiguredServerUrl
        {
            get
            {
                var flavor = ConfiguredFlavor;
                if (string.IsNullOrWhiteSpace(Server.Host) && Server.Port == null && flavor == null)
                    return null;

                var host = string.IsNullOrWhiteSpace(Server.Host) ? "localhost" : Server.Host.Trim();
                var port = Server.Port ?? (flavor == ServerFlavor.XOllama ? OllamaServer.XOllamaDefaultPort : OllamaServer.OllamaDefaultPort);
                return OllamaServer.ToClientUrl($"{host}:{port}", port);
            }
        }
    }
}
