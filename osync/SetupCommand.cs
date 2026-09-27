namespace osync
{
    /// <summary>
    /// `osync setup`: the preferences in the settings file, by section.
    ///   osync setup                              summary, then a menu (in a terminal)
    ///   osync setup show                         summary
    ///   osync setup server [ollama|xollama|both|auto] [host[:port]] [default]
    ///   osync setup alias [list | add NAME ADDRESS | remove NAME]
    ///   osync setup manage [theme [NAME] | sort [ORDER] | themes]
    ///   osync setup shell [theme [NAME|plain] | colors [MODE] | completion | themes]
    /// Without the last arguments a section asks interactively (numbered choices, Enter keeps the current value).
    /// </summary>
    internal sealed class SetupCommand
    {
        private readonly OsyncProgram _program;
        private readonly TextReader _input;
        private readonly TextWriter _output;
        private readonly Func<string, ServerSetup.Probe> _probe;

        public SetupCommand(OsyncProgram program, TextReader input, TextWriter output, Func<string, ServerSetup.Probe> probe)
        {
            _program = program;
            _input = input;
            _output = output;
            _probe = probe;
        }

        private static OsyncSettings Settings => OsyncSettings.Current;

        /// <summary>Runs `osync setup ...`; returns the exit code.</summary>
        public int Run(string? section, string? item, string? name, string? value)
        {
            switch (Lower(section))
            {
                case "":
                    PrintSummary();
                    return Menu();
                case "show":
                case "list":
                    PrintSummary();
                    return 0;
                case "server":
                case "servers":
                    return Server(item, name, value);
                case "alias":
                case "aliases":
                    return Alias(item, name, value);
                case "manage":
                    return Manage(item, name);
                case "shell":
                    return Shell(item, name);
                default:
                    return Fail($"unknown setup section '{section}' (server, alias, manage, shell, show)");
            }
        }

        private static string Lower(string? s) => (s ?? "").Trim().ToLowerInvariant();

        private int Fail(string message)
        {
            _output.WriteLine($"{Out.Paint("Error:", p => p.Error, bold: true)} {Out.Paint(message, p => p.Error)}");
            return 1;
        }

        private void Ok(string message) => _output.WriteLine($"{Out.Paint("✓", p => p.Success, bold: true)} {message}");

        private int Save(string message)
        {
            try
            {
                Settings.SaveAsCurrent();
            }
            catch (Exception ex)
            {
                return Fail($"cannot save {OsyncSettings.FilePath}: {ex.Message}");
            }
            OllamaServer.ResetLocal();
            ColorSupport.Reset();
            Out.Reset();
            Ok(message);
            return 0;
        }

        // ------------------------------------------------------------------------------------------------
        // Summary and menu
        // ------------------------------------------------------------------------------------------------

        public void PrintSummary()
        {
            var s = Settings;
            void Row(string label, string text) => _output.WriteLine($"  {Out.Heading(label.PadRight(14))}{text}");

            _output.WriteLine($"{Out.Heading("osync settings")} {Out.Muted(OsyncSettings.FilePath + (File.Exists(OsyncSettings.FilePath) ? "" : " (not created yet)"))}");
            Row("Local server", ServerSetup.Describe(s));
            var variable = OllamaServer.EnvironmentServerVariable(Environment.GetEnvironmentVariable);
            if (variable != null)
                Row("", OllamaServer.OverridingEnvironmentVariable(Environment.GetEnvironmentVariable, s) != null
                    ? Out.Paint($"{variable}={Environment.GetEnvironmentVariable(variable)} takes precedence", p => p.Warning) + Out.Muted(" (osync setup server env ignore)")
                    : Out.Muted($"{variable}={Environment.GetEnvironmentVariable(variable)} is ignored (osync setup server env use)"));
            if (s.Aliases.Count == 0)
                Row("Aliases", Out.Muted("none (osync setup alias add NAME ADDRESS)"));
            else
                foreach (var (alias, url) in s.Aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
                    Row(alias == s.Aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase).First().Key ? "Aliases" : "",
                        $"{Out.Paint(alias, p => p.Heading)} = {Out.Server(url)}");
            Row("Colors", $"{s.ColorMode} (now {ColorSupport.DisplayName(ColorSupport.Current)}, {ColorSupport.Reason})");
            Row("Shell theme", string.IsNullOrWhiteSpace(s.Shell.Theme) ? $"default ({Themes.DefaultForShell(Environment.GetEnvironmentVariable).Name})" : s.Shell.Theme!);
            Row("Manage", $"theme {Themes.Find(s.Manage.Theme).Name}, sort {ManageSortOrders.Name(ManageSortOrders.Parse(s.Manage.Sort) ?? SortOrder.AlphabeticalAsc)}");
            var managed = ManageServers.Targets(s, localUrl: null).Skip(1).Select(t => t.Name).ToList();
            Row("", "servers: local" + (managed.Count > 0 ? ", " + string.Join(", ", managed) : "") + Out.Muted(" (Ctrl+Left/Right in manage)"));
            _output.WriteLine();
        }

        private int Menu()
        {
            if (System.Console.IsInputRedirected)
            {
                _output.WriteLine("Change with: osync setup server | alias | manage | shell (osync setup -h for details)");
                return 0;
            }
            while (true)
            {
                var answer = ServerSetup.Ask(_input, _output,
                    $"Configure: 1) server  2) aliases  3) manage  4) shell  [Enter = done]: ",
                    a => a is "1" or "2" or "3" or "4");
                int code = answer switch
                {
                    null or "" => -1,
                    "1" => Server(null, null, null),
                    "2" => Alias(null, null, null),
                    "3" => Manage(null, null),
                    _ => Shell(null, null)
                };
                if (code == -1) return 0;
                _output.WriteLine();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // server
        // ------------------------------------------------------------------------------------------------

        private int Server(string? type, string? address, string? defaultServer)
        {
            var s = Settings;
            switch (Lower(type))
            {
                case "":
                {
                    if (!ServerSetup.Configure(s, _input, _output, _probe))
                    {
                        _output.WriteLine("No changes.");
                        return 0;
                    }
                    var code = Save($"Local server: {ServerSetup.Describe(s)}");
                    if (code != 0 || s.Aliases.Count == 0) return code;
                    var servers = ChooseManageServers();
                    if (servers == null) return 0;
                    s.Manage.Servers = servers;
                    return Save($"Manage servers: {DescribeManageServers(s)}");
                }

                case "env":
                case "environment":
                {
                    var variable = OllamaServer.EnvironmentServerVariable(Environment.GetEnvironmentVariable) ?? "XOLLAMA_HOST / OLLAMA_HOST";
                    switch (Lower(address))
                    {
                        case "ignore":
                        case "settings":
                            if (s.ConfiguredServerUrl == null)
                                return Fail("configure a server first (osync setup server), the environment decides until then");
                            s.Server.IgnoreEnvironment = true;
                            return Save($"The settings take precedence over {variable}");
                        case "use":
                        case "prefer":
                        case "environment":
                            s.Server.IgnoreEnvironment = null;
                            return Save($"{variable} takes precedence over the settings");
                        default:
                            return Fail("usage: osync setup server env ignore|use");
                    }
                }

                case "auto":
                    s.Server.Flavor = "auto";
                    s.Server.Host = null;
                    s.Server.Port = null;
                    if (s.Server.Both == true)
                    {
                        s.Aliases.Remove(ServerSetup.OllamaAlias);
                        s.Aliases.Remove(ServerSetup.XOllamaAlias);
                    }
                    s.Server.Both = null;
                    return Save("Local server: auto-detect (localhost:11434, then localhost:22434)");

                case "ollama":
                case "xollama":
                {
                    var flavor = Lower(type) == "xollama" ? ServerFlavor.XOllama : ServerFlavor.Ollama;
                    if (!TryParseHostPort(address, ServerSetup.DefaultPort(flavor), out var host, out var port, out var error))
                        return Fail(error!);
                    if (s.Server.Both == true)
                    {
                        s.Aliases.Remove(ServerSetup.OllamaAlias);
                        s.Aliases.Remove(ServerSetup.XOllamaAlias);
                    }
                    s.Server.Flavor = ServerSetup.FlavorName(flavor);
                    s.Server.Host = host;
                    s.Server.Port = port == ServerSetup.DefaultPort(flavor) ? null : port;
                    s.Server.Both = null;
                    var code = Save($"Local server: {ServerSetup.Describe(s)}");
                    ReportReachable(ServerSetup.Url(host, port));
                    return code;
                }

                case "both":
                {
                    if (!TryParseHostPort(address, 0, out var host, out _, out var error))
                        return Fail(error!);
                    var defaultFlavor = Lower(defaultServer) switch
                    {
                        "" or "ollama" => (ServerFlavor?)ServerFlavor.Ollama,
                        "xollama" => ServerFlavor.XOllama,
                        _ => null
                    };
                    if (defaultFlavor == null)
                        return Fail($"the default server is ollama or xollama, not '{defaultServer}'");
                    var ollamaUrl = ServerSetup.Url(host, OllamaServer.OllamaDefaultPort);
                    var xollamaUrl = ServerSetup.Url(host, OllamaServer.XOllamaDefaultPort);
                    s.Server.Flavor = ServerSetup.FlavorName(defaultFlavor.Value);
                    s.Server.Host = host;
                    s.Server.Port = null;
                    s.Server.Both = true;
                    s.Aliases[ServerSetup.OllamaAlias] = ollamaUrl;
                    s.Aliases[ServerSetup.XOllamaAlias] = xollamaUrl;
                    var code = Save($"Local servers: {ServerSetup.Describe(s)}");
                    ReportReachable(ollamaUrl);
                    ReportReachable(xollamaUrl);
                    return code;
                }

                default:
                    return Fail($"unknown server type '{type}' (ollama, xollama, both, auto)");
            }
        }

        /// <summary>"host", "host:port" or empty (localhost); <paramref name="defaultPort"/> 0 = no port allowed.</summary>
        private static bool TryParseHostPort(string? text, int defaultPort, out string host, out int port, out string? error)
        {
            host = "localhost";
            port = defaultPort;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) return true;

            var value = text.Trim();
            if (value.Contains("://"))
            {
                if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) { error = $"'{text}' is not a valid address"; return false; }
                host = uri.Host;
                var authority = value[(value.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
                if (authority.Contains(':')) port = uri.Port;
                return true;
            }

            var colon = value.LastIndexOf(':');
            if (colon > 0 && !value.EndsWith("]") && value.Count(c => c == ':') == 1)
            {
                if (!int.TryParse(value[(colon + 1)..], out port) || port is < 1 or > 65535)
                {
                    error = $"'{value[(colon + 1)..]}' is not a valid port";
                    return false;
                }
                if (defaultPort == 0)
                {
                    error = "both servers use their default ports (11434 and 22434); give only the host, or run 'osync setup server' to choose the ports";
                    return false;
                }
                value = value[..colon];
            }
            if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
            {
                error = $"'{value}' is not a valid host name or IP address";
                return false;
            }
            host = value;
            return true;
        }

        private void ReportReachable(string url)
        {
            var probe = _probe(url);
            if (probe.Reachable)
                _output.WriteLine($"  {OllamaServer.DisplayName(probe.Flavor)} {probe.Version} answers at {Out.Server(url)}");
            else
                _output.WriteLine($"  {Out.Paint("Warning:", p => p.Warning, bold: true)} no server answers at {url} right now");
        }

        // ------------------------------------------------------------------------------------------------
        // alias
        // ------------------------------------------------------------------------------------------------

        private int Alias(string? action, string? name, string? address)
        {
            var s = Settings;
            switch (Lower(action))
            {
                case "":
                case "list":
                    ListAliases();
                    if (Lower(action) == "" && !System.Console.IsInputRedirected)
                        return AliasMenu();
                    return 0;

                case "add":
                case "set":
                {
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
                        return Fail("usage: osync setup alias add NAME ADDRESS (e.g. osync setup alias add gpu 192.168.1.10)");
                    var invalid = ServerAliases.ValidateName(name.Trim());
                    if (invalid != null) return Fail(invalid);
                    var url = AliasUrl(address, name.Trim());
                    if (url == null) return Fail($"'{address}' is not a valid server address");
                    s.Aliases[name.Trim()] = url;
                    var code = Save($"Alias {Out.Paint(name.Trim(), p => p.Heading)} = {Out.Server(url)}");
                    ReportReachable(url);
                    return code;
                }

                case "remove":
                case "rm":
                case "delete":
                case "del":
                    if (string.IsNullOrWhiteSpace(name))
                        return Fail("usage: osync setup alias remove NAME");
                    var existing = s.Aliases.Keys.FirstOrDefault(k => string.Equals(k, name.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (existing == null) return Fail($"no alias named '{name}'");
                    s.Aliases.Remove(existing);
                    s.Manage.Servers?.RemoveAll(n => string.Equals(n, existing, StringComparison.OrdinalIgnoreCase));
                    return Save($"Removed alias {existing}");

                default:
                    // "osync setup alias gpu 192.168.1.10" = add
                    if (!string.IsNullOrWhiteSpace(action) && !string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(address))
                        return Alias("add", action, name);
                    return Fail($"unknown alias action '{action}' (list, add, remove)");
            }
        }

        /// <summary>Normalized URL of an alias target (scheme and port added; the target itself may not be an alias).</summary>
        private static string? AliasUrl(string address, string aliasName)
        {
            var value = address.Trim().TrimEnd('/');
            if (value.Length == 0 || string.Equals(value, aliasName, StringComparison.OrdinalIgnoreCase)) return null;
            if (ServerAliases.TryExpand(value, out var target)) value = target;
            var url = OsyncProgram.NormalizeServerUrl(value);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null;
            if (uri.AbsolutePath.Trim('/').Length > 0) return null;
            return $"{uri.Scheme}://{uri.Authority}";
        }

        private void ListAliases()
        {
            var aliases = Settings.Aliases;
            if (aliases.Count == 0)
            {
                _output.WriteLine(Out.Muted("No aliases. Add one with: osync setup alias add NAME ADDRESS"));
                return;
            }
            var width = Math.Max(8, aliases.Keys.Max(k => k.Length) + 2);
            _output.WriteLine(Out.Heading($"{"ALIAS".PadRight(width)}SERVER"));
            foreach (var (alias, url) in aliases.OrderBy(a => a.Key, StringComparer.OrdinalIgnoreCase))
                _output.WriteLine($"{Out.Paint(alias.PadRight(width), p => p.Text)}{Out.Server(url)}");
            _output.WriteLine(Out.Muted("Use an alias wherever a server goes: osync ls -d NAME, osync cp model NAME/, osync manage NAME"));
        }

        private int AliasMenu()
        {
            while (true)
            {
                var answer = ServerSetup.Ask(_input, _output, "a) add  r) remove  [Enter = done]: ", a => a is "a" or "r" or "add" or "remove");
                if (string.IsNullOrEmpty(answer)) return 0;
                if (answer is "a" or "add")
                {
                    var name = ServerSetup.Ask(_input, _output, "Alias name: ", a => ServerAliases.ValidateName(a) == null);
                    if (string.IsNullOrEmpty(name)) continue;
                    var address = ServerSetup.Ask(_input, _output, "Server (host, host:port or URL): ", a => AliasUrl(a, name) != null);
                    if (string.IsNullOrEmpty(address)) continue;
                    Alias("add", name, address);
                }
                else
                {
                    var name = ServerSetup.Ask(_input, _output, "Alias to remove: ",
                        a => Settings.Aliases.Keys.Any(k => string.Equals(k, a, StringComparison.OrdinalIgnoreCase)));
                    if (string.IsNullOrEmpty(name)) continue;
                    Alias("remove", name, null);
                }
                ListAliases();
            }
        }

        // ------------------------------------------------------------------------------------------------
        // manage
        // ------------------------------------------------------------------------------------------------

        private int Manage(string? item, string? value)
        {
            var s = Settings;
            switch (Lower(item))
            {
                case "":
                {
                    var theme = ChooseTheme("Manage theme", Themes.Find(s.Manage.Theme), manage: true);
                    if (theme == null) return 0;
                    var sort = ChooseSort();
                    if (sort == null) return 0;
                    s.Manage.Theme = theme.Name;
                    s.Manage.Sort = ManageSortOrders.Name(sort.Value);
                    if (s.Aliases.Count > 0)
                    {
                        var servers = ChooseManageServers();
                        if (servers == null) return 0;
                        s.Manage.Servers = servers;
                    }
                    return Save($"Manage: theme {theme.Name}, sort {s.Manage.Sort}, servers {DescribeManageServers(s)}");
                }

                case "servers":
                case "server":
                {
                    List<string>? servers;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        servers = ChooseManageServers();
                        if (servers == null) return 0;
                    }
                    else if (!TryParseManageServers(value, out servers, out var error))
                    {
                        return Fail(error!);
                    }
                    s.Manage.Servers = servers;
                    return Save($"Manage servers: {DescribeManageServers(s)}");
                }

                case "themes":
                    ListThemes(manage: true, Themes.Find(s.Manage.Theme).Name);
                    return 0;

                case "theme":
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        var chosen = ChooseTheme("Manage theme", Themes.Find(s.Manage.Theme), manage: true);
                        if (chosen == null) return 0;
                        value = chosen.Name;
                    }
                    var theme = Themes.FindExact(value);
                    if (theme == null) return Fail($"unknown theme '{value}' (osync setup manage themes lists them)");
                    s.Manage.Theme = theme.Name;
                    return Save($"Manage theme: {theme.Name}");
                }

                case "sort":
                {
                    SortOrder? order = ManageSortOrders.Parse(value);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        order = ChooseSort();
                        if (order == null) return 0;
                    }
                    if (order == null)
                        return Fail($"unknown sort order '{value}' ({string.Join(", ", ManageSortOrders.All.Select(o => o.Name))})");
                    s.Manage.Sort = ManageSortOrders.Name(order.Value);
                    return Save($"Manage sort: {s.Manage.Sort}");
                }

                default:
                    return Fail($"unknown manage setting '{item}' (theme, sort, servers, themes)");
            }
        }

        private static string DescribeManageServers(OsyncSettings s) =>
            string.Join(", ", ManageServers.Targets(s, localUrl: null).Select(t => t.Name));

        /// <summary>"gpu,nas", "gpu nas", "all" or "none" → alias names (existing aliases only).</summary>
        private bool TryParseManageServers(string text, out List<string>? servers, out string? error)
        {
            servers = null;
            error = null;
            var aliases = Settings.Aliases;
            var value = Lower(text);
            if (value == "none") { servers = new List<string>(); return true; }
            if (value == "all") { servers = aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList(); return true; }

            var result = new List<string>();
            var names = aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var part in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string? name = int.TryParse(part, out var number) && number >= 1 && number <= names.Count
                    ? names[number - 1]
                    : names.FirstOrDefault(n => string.Equals(n, part, StringComparison.OrdinalIgnoreCase));
                if (name == null)
                {
                    error = $"no alias named '{part}' (osync setup alias lists them)";
                    return false;
                }
                if (!result.Contains(name, StringComparer.OrdinalIgnoreCase)) result.Add(name);
            }
            servers = result;
            return true;
        }

        /// <summary>Asks which aliases manage switches between; null at the end of input.</summary>
        private List<string>? ChooseManageServers()
        {
            var s = Settings;
            var names = s.Aliases.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            var current = ManageServers.Targets(s, localUrl: null).Skip(1).Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _output.WriteLine(Out.Heading("Servers in manage") + Out.Muted(" (Ctrl+Left / Ctrl+Right switch between the local server and these)"));
            for (int i = 0; i < names.Count; i++)
            {
                var marker = current.Contains(names[i]) ? Out.Paint("*", p => p.Success, bold: true) : " ";
                _output.WriteLine($" {marker}{(i + 1),2}) {Out.Paint(names[i].PadRight(14), p => p.Text)}{Out.Server(s.Aliases[names[i]])}");
            }
            if (s.Server.Both == true)
                _output.WriteLine(Out.Muted($"     (with Ollama and xOllama side by side, the other one is always included)"));

            var currentText = current.Count == 0 ? "none" : string.Join(",", names.Where(current.Contains));
            var answer = ServerSetup.Ask(_input, _output, $"Aliases for manage (numbers or names, all, none) [{currentText}]: ",
                a => TryParseManageServers(a, out _, out _));
            if (answer == null) return null;
            if (answer.Length == 0) return names.Where(current.Contains).ToList();
            TryParseManageServers(answer, out var servers, out _);
            return servers;
        }

        private SortOrder? ChooseSort()
        {
            var current = ManageSortOrders.Parse(Settings.Manage.Sort) ?? SortOrder.AlphabeticalAsc;
            _output.WriteLine(Out.Heading("Default sort order of manage"));
            for (int i = 0; i < ManageSortOrders.All.Length; i++)
            {
                var (order, name, description) = ManageSortOrders.All[i];
                var marker = order == current ? Out.Paint("*", p => p.Success, bold: true) : " ";
                _output.WriteLine($" {marker}{(i + 1),2}) {Out.Paint(name.PadRight(10), p => p.Text)}{Out.Muted(description)}");
            }
            var answer = ServerSetup.Ask(_input, _output, $"Sort order [{ManageSortOrders.Name(current)}]: ",
                a => (int.TryParse(a, out var n) && n >= 1 && n <= ManageSortOrders.All.Length) || ManageSortOrders.Parse(a) != null);
            if (answer == null) return null;
            if (answer.Length == 0) return current;
            return int.TryParse(answer, out var number) ? ManageSortOrders.All[number - 1].Order : ManageSortOrders.Parse(answer);
        }

        // ------------------------------------------------------------------------------------------------
        // shell
        // ------------------------------------------------------------------------------------------------

        private int Shell(string? item, string? value)
        {
            var s = Settings;
            switch (Lower(item))
            {
                case "":
                {
                    var current = string.IsNullOrWhiteSpace(s.Shell.Theme) ? null : s.Shell.Theme;
                    var theme = ChooseTheme("Shell theme", Themes.FindExact(current) ?? Themes.DefaultForShell(Environment.GetEnvironmentVariable),
                        manage: false, allowPlain: true, currentIsPlain: string.Equals(current, Out.PlainTheme, StringComparison.OrdinalIgnoreCase));
                    if (theme == null) return 0;
                    s.Shell.Theme = theme.Name;
                    var mode = ChooseColorMode();
                    if (mode == null) return 0;
                    s.ColorMode = mode;
                    var code = Save($"Shell: theme {s.Shell.Theme}, colors {s.ColorMode}");
                    if (code != 0) return code;
                    var completion = ServerSetup.Ask(_input, _output, "Install tab completion for your shell? (y/N): ", a => a is "y" or "n" or "yes" or "no");
                    return completion is "y" or "yes" ? InstallCompletion() : 0;
                }

                case "themes":
                    ListThemes(manage: false, string.IsNullOrWhiteSpace(s.Shell.Theme) ? Themes.DefaultForShell(Environment.GetEnvironmentVariable).Name : s.Shell.Theme!);
                    return 0;

                case "theme":
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        var chosen = ChooseTheme("Shell theme", Themes.FindExact(s.Shell.Theme) ?? Themes.DefaultForShell(Environment.GetEnvironmentVariable),
                            manage: false, allowPlain: true, currentIsPlain: string.Equals(s.Shell.Theme, Out.PlainTheme, StringComparison.OrdinalIgnoreCase));
                        if (chosen == null) return 0;
                        value = chosen.Name;
                    }
                    if (Lower(value) is "plain" or "none" or "off")
                    {
                        s.Shell.Theme = Out.PlainTheme;
                        return Save("Shell theme: plain (no colors)");
                    }
                    if (Lower(value) is "default" or "auto")
                    {
                        s.Shell.Theme = null;
                        return Save($"Shell theme: default ({Themes.DefaultForShell(Environment.GetEnvironmentVariable).Name})");
                    }
                    var theme = Themes.FindExact(value);
                    if (theme == null) return Fail($"unknown theme '{value}' (osync setup shell themes lists them)");
                    s.Shell.Theme = theme.Name;
                    return Save($"Shell theme: {theme.Name}");
                }

                case "colors":
                case "color":
                case "colormode":
                {
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        value = ChooseColorMode();
                        if (value == null) return 0;
                    }
                    var mode = Lower(value) == "auto" ? "auto" : ColorSupport.Parse(value) switch
                    {
                        ColorDepth.TrueColor => "truecolor",
                        ColorDepth.Colors256 => "256",
                        ColorDepth.Standard16 => "16",
                        ColorDepth.None => "none",
                        _ => null
                    };
                    if (mode == null) return Fail($"unknown color mode '{value}' (auto, truecolor, 256, 16, none)");
                    s.ColorMode = mode;
                    return Save($"Colors: {mode}");
                }

                case "completion":
                case "integration":
                    return InstallCompletion();

                default:
                    return Fail($"unknown shell setting '{item}' (theme, colors, completion, themes)");
            }
        }

        private string? ChooseColorMode()
        {
            var modes = new[] { "auto", "truecolor", "256", "16", "none" };
            var current = Array.IndexOf(modes, Lower(Settings.ColorMode));
            if (current < 0) current = 0;
            _output.WriteLine(Out.Heading("Colors") + Out.Muted($" (detected: {ColorSupport.DisplayName(ColorSupport.Detect(Environment.GetEnvironmentVariable, "auto", TerminalInitializer.OriginalTerm, OperatingSystem.IsWindows()).Depth)})"));
            var answer = ServerSetup.Ask(_input, _output,
                $"1) auto  2) true color  3) 256  4) 16  5) none [{current + 1}]: ",
                a => (int.TryParse(a, out var n) && n is >= 1 and <= 5) || modes.Contains(a));
            if (answer == null) return null;
            if (answer.Length == 0) return modes[current];
            return int.TryParse(answer, out var number) ? modes[number - 1] : answer;
        }

        private int InstallCompletion()
        {
            if (OperatingSystem.IsWindows())
                _program.InstallPowerShellCompletion();
            else
                _program.InstallBashCompletion();
            return 0;
        }

        // ------------------------------------------------------------------------------------------------
        // Themes: list with previews, choose
        // ------------------------------------------------------------------------------------------------

        private void ListThemes(bool manage, string currentName)
        {
            var depth = Out.Palette == null ? ColorDepth.None : ColorSupport.Current;
            _output.WriteLine(Out.Heading(manage ? "Manage themes" : "Shell themes") +
                              Out.Muted($" ({ColorSupport.DisplayName(ColorSupport.Current)})"));
            for (int i = 0; i < Themes.All.Count; i++)
            {
                var theme = Themes.All[i];
                if (i == 0 || theme.IsLight != Themes.All[i - 1].IsLight)
                    _output.WriteLine(Out.Muted(theme.IsLight ? "  for light backgrounds:" : "  for dark backgrounds:"));
                var marker = string.Equals(theme.Name, currentName, StringComparison.OrdinalIgnoreCase) ? Out.Paint("*", p => p.Success, bold: true) : " ";
                var label = $"{(i + 1),2}) {theme.Name}";
                var preview = manage ? ManagePreview(theme, depth) : ShellPreview(theme, depth);
                _output.WriteLine($" {marker}{label.PadRight(24)}{preview}");
            }
            if (!manage)
            {
                var marker = string.Equals(currentName, Out.PlainTheme, StringComparison.OrdinalIgnoreCase) ? Out.Paint("*", p => p.Success, bold: true) : " ";
                _output.WriteLine($" {marker}{"    plain".PadRight(24)}no colors");
            }
        }

        private OsyncTheme? ChooseTheme(string title, OsyncTheme current, bool manage, bool allowPlain = false, bool currentIsPlain = false)
        {
            ListThemes(manage, currentIsPlain ? Out.PlainTheme : current.Name);
            var answer = ServerSetup.Ask(_input, _output, $"{title} (number or name) [{(currentIsPlain ? Out.PlainTheme : current.Name)}]: ",
                a => Themes.FindExact(a) != null || (allowPlain && Lower(a) is "plain" or "none"));
            if (answer == null) return null;
            if (answer.Length == 0)
                return currentIsPlain ? PlainMarker : current;
            if (allowPlain && Lower(answer) is "plain" or "none") return PlainMarker;
            return Themes.FindExact(answer);
        }

        /// <summary>Stands for the "plain" shell theme in <see cref="ChooseTheme"/>.</summary>
        private static readonly OsyncTheme PlainMarker = Themes.Monochrome with { Name = Out.PlainTheme };

        /// <summary>A sample line of command output in <paramref name="theme"/>'s shell colors.</summary>
        internal static string ShellPreview(OsyncTheme theme, ColorDepth depth)
        {
            var p = Themes.ForShell(theme, depth);
            string P(string text, Func<ShellPalette, ShellColor> role, bool bold = false) => Out.Paint(p, text, role, bold);
            return $"{P("qwen3:8b", x => x.Text)}  {P("a80c4f17acd5", x => x.Id)}  {P("5.2 GB", x => x.Size)}  " +
                   $"{P("8B", x => x.Params)}  {P("Q4_K_M", x => x.Quant)}  {P("✓ done", x => x.Success, true)}  " +
                   $"{P("Warning", x => x.Warning, true)}  {P("Error", x => x.Error, true)}";
        }

        /// <summary>A sample row of the manage list on <paramref name="theme"/>'s background.</summary>
        internal static string ManagePreview(OsyncTheme theme, ColorDepth depth)
        {
            if (depth == ColorDepth.None) return "qwen3:8b  5.2GB  8B  Q4_K_M  qwen3  ● selected";
            var t = Themes.Adapt(theme, depth);
            string Seg(string text, Rgb fg, Rgb bg, bool bold = false) =>
                $"\u001b[{(bold ? "1;" : "")}{Sgr(fg, depth, background: false)};{Sgr(bg, depth, background: true)}m{text}\u001b[0m";
            return Seg(" qwen3:8b ", t.Text, t.Background) + Seg(" 5.2GB ", t.Size, t.Background) + Seg("8B ", t.Params, t.Background) +
                   Seg("Q4_K_M ", t.Quant, t.Background) + Seg("qwen3 ", t.Family, t.Background) + Seg(" ● ", t.Loaded, t.Background) +
                   Seg(" selected ", t.SelectionText, t.SelectionBackground, bold: true) + Seg(" bar ", t.BarAccent, t.BarBackground);
        }

        /// <summary>SGR parameters of a color adapted by <see cref="Themes.Adapt"/> at <paramref name="depth"/>.</summary>
        private static string Sgr(Rgb color, ColorDepth depth, bool background)
        {
            switch (depth)
            {
                case ColorDepth.Standard16:
                {
                    var index = Array.FindIndex(Themes.Ansi16, a => a.Anchor == color);
                    if (index < 0) index = Themes.NearestAnsi16(color);
                    var code = Themes.Ansi16ForegroundCode(index);
                    return (background ? code + 10 : code).ToString();
                }
                case ColorDepth.Colors256:
                    return $"{(background ? 48 : 38)};5;{Themes.Xterm256Index(color)}";
                default:
                    return $"{(background ? 48 : 38)};2;{color.R};{color.G};{color.B}";
            }
        }
    }
}
