using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace osync
{
    /// <summary>A release version: v1.4.6 or a dev pre-release v1.4.6-dev.93 (semver ordering).</summary>
    internal readonly record struct ReleaseVersion(int Major, int Minor, int Patch, string? Pre) : IComparable<ReleaseVersion>
    {
        private static readonly Regex Pattern = new(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$", RegexOptions.CultureInvariant);

        public bool IsPreRelease => Pre != null;

        public static bool TryParse(string? text, out ReleaseVersion version)
        {
            version = default;
            var match = Pattern.Match(text?.Trim() ?? "");
            if (!match.Success) return false;
            version = new ReleaseVersion(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value),
                match.Groups[4].Success ? match.Groups[4].Value : null);
            return true;
        }

        public int CompareTo(ReleaseVersion other)
        {
            int c = Major.CompareTo(other.Major);
            if (c == 0) c = Minor.CompareTo(other.Minor);
            if (c == 0) c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;
            if (Pre == null || other.Pre == null) return (Pre == null ? 1 : 0) - (other.Pre == null ? 1 : 0);

            // Pre-release identifiers: numeric ones compare as numbers (dev.9 < dev.10), the rest as text
            var a = Pre.Split('.');
            var b = other.Pre.Split('.');
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
            {
                bool an = long.TryParse(a[i], out var av), bn = long.TryParse(b[i], out var bv);
                c = an && bn ? av.CompareTo(bv) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
                if (c != 0) return c;
            }
            return a.Length.CompareTo(b.Length);
        }

        public static bool operator >(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) > 0;
        public static bool operator <(ReleaseVersion a, ReleaseVersion b) => a.CompareTo(b) < 0;

        public override string ToString() => $"v{Major}.{Minor}.{Patch}{(Pre != null ? "-" + Pre : "")}";
    }

    internal sealed record ReleaseAsset(string Name, string DownloadUrl, long Size, string? Digest);

    internal sealed record ReleaseInfo(string Tag, ReleaseVersion Version, bool PreRelease, string? HtmlUrl, IReadOnlyList<ReleaseAsset> Assets);

    /// <summary>osync releases on GitHub (mann1x/osync): parsing, choosing the newest, the binary for this platform.</summary>
    internal static class OsyncReleases
    {
        public const string Repository = "mann1x/osync";
        public static string ApiUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=50";

        /// <summary>Parses the GitHub releases API answer; drafts and tags that are not versions are left out.</summary>
        public static List<ReleaseInfo> Parse(string json)
        {
            var releases = new List<ReleaseInfo>();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return releases;
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                if (r.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True) continue;
                var tag = r.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
                if (!ReleaseVersion.TryParse(tag, out var version)) continue;
                bool pre = (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) || version.IsPreRelease;
                var assets = new List<ReleaseAsset>();
                if (r.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in list.EnumerateArray())
                    {
                        var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                        var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                        long size = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sv) ? sv : 0;
                        var digest = a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                        assets.Add(new ReleaseAsset(name, url, size, digest));
                    }
                }
                releases.Add(new ReleaseInfo(tag!, version, pre, r.TryGetProperty("html_url", out var h) ? h.GetString() : null, assets));
            }
            return releases;
        }

        /// <summary>Pre-releases are considered with --pre or when the running osync is itself a pre-release.</summary>
        public static bool IncludePreReleases(bool preFlag, ReleaseVersion running) => preFlag || running.IsPreRelease;

        /// <summary>The newest release; pre-releases only when <paramref name="includePreReleases"/>.</summary>
        public static ReleaseInfo? Newest(IEnumerable<ReleaseInfo> releases, bool includePreReleases) =>
            releases.Where(r => includePreReleases || !r.PreRelease).OrderByDescending(r => r.Version).FirstOrDefault();

        /// <summary>Name of the release binary for an OS and process architecture: the `asset` names of the package matrix in ci.yml (the `#label` after them in `gh release create` is only the display text).</summary>
        public static string? AssetName(OSPlatform os, Architecture arch)
        {
            if (os == OSPlatform.Windows && arch == Architecture.X64) return "osync.exe";
            if (os == OSPlatform.Linux && arch == Architecture.X64) return "osync";
            if (os == OSPlatform.OSX && arch == Architecture.Arm64) return "osync-macos-arm64";
            if (os == OSPlatform.OSX && arch == Architecture.X64) return "osync-macos-x64";
            return null;
        }

        public static string? CurrentAssetName()
        {
            var os = OperatingSystem.IsWindows() ? OSPlatform.Windows : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
            return AssetName(os, RuntimeInformation.ProcessArchitecture);
        }

        /// <summary>Version in the output of `osync ShowVersion` ("osync v1.4.6-dev.93 (b20261016-1000)").</summary>
        public static ReleaseVersion? ParseVersionOutput(string output)
        {
            var match = Regex.Match(output, @"osync\s+(v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)", RegexOptions.IgnoreCase);
            return match.Success && ReleaseVersion.TryParse(match.Groups[1].Value, out var v) ? v : null;
        }
    }

    /// <summary>
    /// `osync upgrade`: checks the GitHub releases for a newer osync and replaces the binary. With osync installed
    /// (`osync install`: ~/.osync on Windows, ~/.local/bin elsewhere) the installed binary is upgraded and the new
    /// binary's `install` runs again with the choices made at install time; otherwise the running binary is replaced.
    /// </summary>
    internal sealed class UpgradeCommand
    {
        private readonly TextReader _input;
        private readonly TextWriter _output;

        public UpgradeCommand(TextReader input, TextWriter output)
        {
            _input = input;
            _output = output;
        }

        /// <summary>Asks a yes/no question; an empty answer gives <paramref name="defaultYes"/>, the end of the input null.</summary>
        internal static bool? AskYesNo(TextReader input, TextWriter output, string question, bool defaultYes)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                output.Write($"{question} {(defaultYes ? "(Y/n)" : "(y/N)")}: ");
                var line = input.ReadLine();
                if (line == null) { output.WriteLine(); return null; }
                switch (line.Trim().ToLowerInvariant())
                {
                    case "": return defaultYes;
                    case "y" or "yes": return true;
                    case "n" or "no": return false;
                }
                output.WriteLine("  Please answer y or n.");
            }
            return defaultYes;
        }

        /// <summary>Removes the binary a Windows upgrade renamed aside (it cannot be deleted while it runs).</summary>
        internal static void CleanupPreviousBinary()
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var path = Environment.ProcessPath;
                if (path == null) return;
                foreach (var old in new[] { path + ".old", Path.Combine(OsyncProgram.InstallDirectory, "osync.exe.old") })
                    if (File.Exists(old)) File.Delete(old);
            }
            catch
            {
                // still running (an upgrade in progress) or not ours to delete: next time
            }
        }

        public int Run(bool yes, bool check, bool includePre)
        {
            CleanupPreviousBinary();

            string exeName = OperatingSystem.IsWindows() ? "osync.exe" : "osync";
            string installedPath = Path.Combine(OsyncProgram.InstallDirectory, exeName);
            string? runningPath = OsyncProgram.RunningBinaryPath();
            bool systemWide = File.Exists(installedPath);
            // null: osync runs through the dotnet host (osync.dll), there is no binary to replace
            string? target = systemWide ? installedPath : runningPath;
            bool targetIsRunning = target != null && runningPath != null && SamePath(runningPath, target);

            // Version of the binary being upgraded
            ReleaseVersion? current = target == null || targetIsRunning ? OsyncProgram.RunningReleaseVersion : VersionOf(target);
            if (target != null) _output.WriteLine($"{(systemWide ? "Installed" : "Binary")}: {target}");
            _output.WriteLine($"Current version: {(current?.ToString() ?? "unknown")}");

            // The channel follows the osync you run: a dev pre-release looks for pre-releases newer than the latest
            // release too, a release only for releases (whatever the installed copy is)
            bool pre = OsyncReleases.IncludePreReleases(includePre, OsyncProgram.RunningReleaseVersion);
            _output.WriteLine($"Channel:         {(pre ? includePre ? "releases and pre-releases (--pre)" : "releases and pre-releases (this osync is a pre-release)" : "releases")}");
            List<ReleaseInfo> releases;
            try
            {
                releases = FetchReleases();
            }
            catch (Exception ex)
            {
                Out.Error($"could not read the osync releases on GitHub ({OsyncReleases.ApiUrl}): {ex.Message}");
                return 1;
            }

            var newest = OsyncReleases.Newest(releases, pre);
            if (newest == null)
            {
                Out.Error($"no osync {(pre ? "release or pre-release" : "release")} found on GitHub");
                return 1;
            }
            if (current != null && !(newest.Version > current.Value))
            {
                Out.Success($"osync is up to date (latest {(pre ? "release or pre-release" : "release")}: {newest.Tag})");
                return 0;
            }

            _output.WriteLine($"New version:     {Out.Paint(newest.Tag, p => p.Success, bold: true)}{(newest.PreRelease ? " (pre-release)" : "")}");
            if (newest.HtmlUrl != null) _output.WriteLine($"Release notes:   {newest.HtmlUrl}");
            if (check) return 0;
            if (target == null)
            {
                Out.Error("osync runs through 'dotnet' (a framework-dependent build): rebuild it from source, or download a release binary");
                return 1;
            }

            var assetName = OsyncReleases.CurrentAssetName();
            if (assetName == null)
            {
                Out.Error($"there is no osync release binary for {RuntimeInformation.OSDescription} {RuntimeInformation.ProcessArchitecture}");
                return 1;
            }
            var asset = newest.Assets.FirstOrDefault(a => a.Name.Equals(assetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                Out.Error($"release {newest.Tag} has no {assetName} binary");
                return 1;
            }

            _output.WriteLine();
            if (!yes)
            {
                var answer = AskYesNo(_input, _output, $"Upgrade osync to {newest.Tag}?", defaultYes: false);
                if (answer != true)
                {
                    _output.WriteLine("Upgrade cancelled.");
                    return 0;
                }
            }

            string targetDir = Path.GetDirectoryName(Path.GetFullPath(target))!;
            string downloaded = Path.Combine(targetDir, OperatingSystem.IsWindows() ? "osync.new.exe" : ".osync.new");
            try
            {
                Download(asset, downloaded);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(downloaded, File.GetUnixFileMode(downloaded) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

                var downloadedVersion = VersionOf(downloaded);
                if (downloadedVersion == null)
                    throw new InvalidOperationException($"the downloaded {assetName} does not run on this machine");

                Replace(downloaded, target, _output);
            }
            catch (Exception ex)
            {
                TryDelete(downloaded);
                Out.Error(ex is UnauthorizedAccessException
                    ? $"cannot write to {targetDir}: {ex.Message} (run the upgrade as a user who can write there)"
                    : $"upgrade failed: {ex.Message}");
                return 1;
            }

            Out.Success($"Upgraded {target} to {newest.Tag}");
            if (systemWide && runningPath != null && !targetIsRunning)
                _output.WriteLine($"The binary you ran ({runningPath}) was left unchanged.");

            if (!systemWide) return 0;

            // Repeat the install with the new binary: it reinstalls what was chosen at install time (shell completion)
            _output.WriteLine();
            return RunInstall(target, yes);
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        private static ReleaseVersion? VersionOf(string binary)
        {
            try
            {
                var psi = new ProcessStartInfo(binary)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("ShowVersion");
                psi.Environment["NO_COLOR"] = "1";
                using var process = Process.Start(psi);
                if (process == null) return null;
                var stdout = process.StandardOutput.ReadToEndAsync();
                _ = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    try { process.Kill(); } catch { }
                    return null;
                }
                return OsyncReleases.ParseVersionOutput(stdout.GetAwaiter().GetResult());
            }
            catch
            {
                return null;
            }
        }

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"osync/{OsyncProgram.AppVersion}");
            return http;
        }

        private static List<ReleaseInfo> FetchReleases()
        {
            using var http = CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, OsyncReleases.ApiUrl);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            // Optional token against the anonymous rate limit (60 requests per hour)
            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
            using var response = http.Send(request);
            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            return OsyncReleases.Parse(body);
        }

        private void Download(ReleaseAsset asset, string path)
        {
            using var http = CreateClient();
            using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"download of {asset.Name} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            long total = response.Content.Headers.ContentLength ?? asset.Size;
            bool progress = !Console.IsOutputRedirected && total > 0;
            _output.WriteLine($"Downloading {asset.Name} ({total / 1048576.0:F1} MB)...");

            using var sha = SHA256.Create();
            long received = 0;
            using (var source = response.Content.ReadAsStream())
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[1 << 16];
                int read, lastPercent = -1;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    received += read;
                    if (progress)
                    {
                        int percent = (int)(received * 100 / total);
                        if (percent != lastPercent) { _output.Write($"\r  {percent,3}%"); lastPercent = percent; }
                    }
                }
            }
            sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            if (progress) _output.WriteLine();

            if (asset.Size > 0 && received != asset.Size)
                throw new InvalidOperationException($"download of {asset.Name} is incomplete ({received} of {asset.Size} bytes)");
            if (asset.Digest != null && asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                var hash = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
                if (!asset.Digest.Substring(7).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"checksum of {asset.Name} does not match the release ({hash} instead of {asset.Digest.Substring(7)})");
            }
        }

        /// <summary>
        /// Puts the new binary in place. Unix: rename over the old one (a running binary keeps its inode).
        /// Windows: a running exe cannot be overwritten but can be renamed, so it is moved aside to osync.exe.old
        /// (deleted by the next run) and the new one takes its name. Each rename is retried for a while: right
        /// after the download and the test run, an antivirus scan (or the indexer) can hold the file open.
        /// </summary>
        internal static void Replace(string newBinary, string target, TextWriter? log = null)
        {
            if (!OperatingSystem.IsWindows())
            {
                MoveWithRetry(newBinary, target, overwrite: true, log);
                return;
            }

            string old = target + ".old";
            TryDelete(old);
            if (File.Exists(old))
                old = target + $".{DateTime.UtcNow:yyyyMMddHHmmss}.old";
            bool movedAside = false;
            if (File.Exists(target))
            {
                MoveWithRetry(target, old, overwrite: false, log);
                movedAside = true;
            }
            try
            {
                MoveWithRetry(newBinary, target, overwrite: false, log);
            }
            catch
            {
                if (movedAside) File.Move(old, target);
                throw;
            }
            if (movedAside) TryDelete(old);
        }

        /// <summary>
        /// Renames <paramref name="from"/> to <paramref name="to"/>, retrying while the file is in use (sharing
        /// violation) for up to <paramref name="attempts"/> × <paramref name="delayMs"/>; the final error names the file
        /// and, on Windows, an osync process running from it.
        /// </summary>
        internal static void MoveWithRetry(string from, string to, bool overwrite, TextWriter? log,
            int attempts = 60, int delayMs = 500, Action<string, string, bool>? move = null)
        {
            move ??= File.Move;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    move(from, to, overwrite);
                    return;
                }
                catch (IOException ex) when (ex is not FileNotFoundException && ex is not DirectoryNotFoundException)
                {
                    if (attempt >= attempts)
                        throw new IOException($"cannot rename {from} to {Path.GetFileName(to)}: {ex.Message}{InUseHint(from)}", ex);
                    if (attempt == 1)
                        log?.WriteLine($"Waiting for {Path.GetFileName(from)} to be released ({ex.Message.TrimEnd('.')})...");
                    Thread.Sleep(delayMs);
                }
            }
        }

        private static string InUseHint(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)))
                {
                    using (process)
                    {
                        string? file = null;
                        try { file = process.MainModule?.FileName; } catch { }
                        if (file != null && string.Equals(Path.GetFullPath(file), full, StringComparison.OrdinalIgnoreCase))
                            return $" (osync is still running from it, process {process.Id}: close it and run the upgrade again)";
                    }
                }
            }
            catch
            {
                // process list not readable
            }
            return " (another program, such as an antivirus scan, has it open: run the upgrade again in a minute)";
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // in use or not ours: left for later
            }
        }

        private static int RunInstall(string binary, bool yes)
        {
            try
            {
                var psi = new ProcessStartInfo(binary) { UseShellExecute = false };
                psi.ArgumentList.Add("install");
                if (yes) psi.ArgumentList.Add("-y");
                using var process = Process.Start(psi);
                if (process == null)
                {
                    Out.Error($"could not start {binary} install");
                    return 1;
                }
                process.WaitForExit();
                return process.ExitCode;
            }
            catch (Exception ex)
            {
                Out.Error($"could not run '{binary} install': {ex.Message}");
                return 1;
            }
        }
    }
}
