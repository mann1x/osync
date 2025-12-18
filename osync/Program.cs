using PowerArgs.Samples;
using PowerArgs;
using System;
using System.Net;
using System.Diagnostics;
using UrlCombineLib;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Reflection.Emit;
using TqdmSharp;
using static PowerArgs.Ansi.Cursor;
using static System.Net.WebRequestMethods;
using System.Net.Http.Json;
using System.IO;
using System.Xml.Linq;
using System.Reflection;
using Born2Code.Net;
using static PrettyConsole.Console;
using Console = PrettyConsole.Console;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Devices.Power;
using Microsoft.VisualBasic;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Drawing;
using Spectre.Console;
using ByteSizeLib;

namespace osync
{
    public class CopyArgs
    {
        [ArgRequired, ArgDescription("Source model to copy eg: mistral:latest"), ArgPosition(1)]
        public string Source { get; set; }
        [ArgRequired, ArgDescription("Destination: local model name (e.g., my-model:v1) or remote server URL (e.g., http://192.168.0.100:11434)"), ArgExample("my-backup-model", "local model name"), ArgExample("http://192.168.0.100:11434", "remote server"), ArgPosition(2)]
        public string Destination { get; set; }
    }

    public class ListArgs
    {
        [ArgDescription("Model pattern, supports * as wildcard, eg: mistral:* or *mistral*"), ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d"), ArgPosition(2)]
        public string Destination { get; set; }
        [ArgDescription("Sort by size (descending)"), ArgShortcut("--size")]
        public bool SortBySize { get; set; }
        [ArgDescription("Sort by size (ascending)"), ArgShortcut("--sizeasc")]
        public bool SortBySizeAsc { get; set; }
        [ArgDescription("Sort by modified time (descending)"), ArgShortcut("--time")]
        public bool SortByTime { get; set; }
        [ArgDescription("Sort by modified time (ascending)"), ArgShortcut("--timeasc")]
        public bool SortByTimeAsc { get; set; }
    }

    public class RemoveArgs
    {
        [ArgRequired, ArgDescription("Model pattern to delete, supports * as wildcard, eg: mistral:* or *mistral*"), ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d"), ArgPosition(2)]
        public string Destination { get; set; }
    }

    public class RenameArgs
    {
        [ArgRequired, ArgDescription("Source model name (e.g., llama3:latest or llama3 for :latest)"), ArgPosition(1)]
        public string Source { get; set; }
        [ArgRequired, ArgDescription("New model name (e.g., my-llama3:v1 or my-llama3 for :latest)"), ArgPosition(2)]
        public string NewName { get; set; }
    }

    public class UpdateArgs
    {
        [ArgDescription("Model pattern to update, supports * as wildcard (e.g., llama* or * for all models). If omitted, updates all models."), ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d"), ArgPosition(2)]
        public string Destination { get; set; }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling), TabCompletion(typeof(LocalModelsTabCompletionSource), HistoryToSave = 10, REPL = true)]
    public class OsyncProgram
    {
        static string version = "1.0.9";
        static HttpClient client = new HttpClient() { Timeout = TimeSpan.FromDays(1) };
        string ollama_models = "";
        long btvalue = 0;
        string separator = Path.DirectorySeparatorChar.ToString();

        public static string GetAppVersion()
        {
            return version;
        }

        [HelpHook, ArgShortcut("-?"), ArgShortcut("-h"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgActionMethod, ArgDescription("Copy a model locally or to a remote server (alias: cp)"), ArgShortcut("cp")]
        public void Copy(CopyArgs args)
        {
            ActionCopy(args.Source, args.Destination);
        }

        [ArgActionMethod, ArgDescription("List the models on a local or remote ollama server (alias: ls)"), ArgShortcut("ls")]
        public void List(ListArgs args)
        {
            string sortMode = "name"; // default
            if (args.SortBySize) sortMode = "size_desc";
            else if (args.SortBySizeAsc) sortMode = "size_asc";
            else if (args.SortByTime) sortMode = "time_desc";
            else if (args.SortByTimeAsc) sortMode = "time_asc";

            ActionList(args.Pattern, args.Destination, sortMode);
        }

        [ArgActionMethod, ArgDescription("Remove models matching pattern locally or on remote server (aliases: rm, delete, del)"), ArgShortcut("rm"), ArgShortcut("delete"), ArgShortcut("del")]
        public void Remove(RemoveArgs args)
        {
            ActionRemove(args.Pattern, args.Destination);
        }

        [ArgActionMethod, ArgDescription("Rename a model by copying to new name and deleting original (aliases: mv, ren)"), ArgShortcut("mv"), ArgShortcut("ren")]
        public void Rename(RenameArgs args)
        {
            ActionRename(args.Source, args.NewName);
        }

        [ArgActionMethod, ArgDescription("Update models to their latest versions locally or on remote server")]
        public void Update(UpdateArgs args)
        {
            ActionUpdate(args.Pattern, args.Destination);
        }

        [ArgShortcut("-bt"), ArgDescription("Bandwidth throttling (B, KB, MB, GB)"), ArgExample("-bt 10MB", "Throttle bandwidth to 10MB"), ArgDefaultValue(0)]
        public string BandwidthThrottling { get; set; }

        public long ParseBandwidthThrottling(string value)
        {
        try
            {
                if (long.TryParse(value, out long result))
                {
                    return result;
                }
                if (value.EndsWith("B", StringComparison.OrdinalIgnoreCase) && Tools.IsNumeric(value.Substring(value.Length-1, 1)))
                {
                    return long.Parse(value.Substring(0, value.Length - 1));
                }

                if (value.EndsWith("KB", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith("MB", StringComparison.OrdinalIgnoreCase)
                    || value.EndsWith("GB", StringComparison.OrdinalIgnoreCase))
                {

                    string numericPart = value.Substring(0, value.Length - 2);
                    long numericValue = long.Parse(numericPart);

                    char unit = value[value.Length - 2];
                    switch (char.ToLower(unit))
                    {
                        case 'k':
                            return numericValue * 1024;
                        case 'm':
                            return numericValue * 1024 * 1024;
                        case 'g':
                            return numericValue * 1024 * 1024 * 1024;
                        default:
                            Console.WriteLine($"Error: invalid bandwidth throttling format (unit={unit}).");
                            System.Environment.Exit(1);
                            return 0;
                    }
                }
                else
                {
                    Console.WriteLine($"Error: invalid bandwidth throttling format.");
                    System.Environment.Exit(1);
                    return 0;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception, invalid bandwidth throttling format: {e.Message}");
                System.Environment.Exit(1);
                return 0;
            }
        }
        public static bool GetPlatformColor()
        {
            string thisOs = System.Environment.OSVersion.Platform.ToString();

            if (thisOs == "Win32NT")
            {
                return false;
            }
            else if (thisOs == "Unix" && System.Environment.OSVersion.VersionString.Contains("Darwin"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public static void SetCursorVisible(bool visible)
        {
            try
            {
                System.Console.CursorVisible = visible;
            }
            catch
            {
                // Ignore - cursor visibility not supported in this terminal
            }
        }
        public bool ValidateServer(string RemoteServer, bool silent = false)
        {
            try
            {

                Uri RemoteUri = new Uri(RemoteServer);
                if ((RemoteUri.Scheme == "http" || RemoteUri.Scheme == "https") == false) { return false; };

                client.BaseAddress = new Uri(RemoteServer);

                RunCheckServer(silent).GetAwaiter().GetResult();

            }
            catch (UriFormatException)
            {
                Console.WriteLine("Error: remote server URL is not valid");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Could not test url {RemoteServer}: {ex.Message}");
                return false;
            }
            return true;
        }

        static async Task<HttpStatusCode> GetPs()
        {
            HttpResponseMessage response = await client.GetAsync(
                $"api/ps");
            return response.StatusCode;
        }

        static async Task RunCheckServer(bool silent = false)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(
                    $"api/version");
                var statusCode = (int)response.StatusCode;

                if (statusCode >= 100 && statusCode < 400)
                {
                    if (!silent)
                    {
                        string versionjson = response.Content.ReadAsStringAsync().Result;
                        RootVersion _version = VersionReader.Read<RootVersion>(versionjson);
                        Console.WriteLine();

                        Console.WriteLine($"The remote ollama server is active and running v{_version.version}");
                    }
                }
                else if (statusCode >= 500 && statusCode <= 510)
                {
                    Console.WriteLine("Error: the remote server has thrown an internal error. ollama instance is not available");
                    System.Environment.Exit(1);
                }
                else
                {
                    Console.WriteLine($"Error: the remote ollama server has answered with HTTP status code: {statusCode}");
                    System.Environment.Exit(1);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        static async Task<HttpStatusCode> BlobHead(string digest)
        {

            HttpRequestMessage request = new(HttpMethod.Head, $"api/blobs/{digest}");
            HttpResponseMessage response = await client.SendAsync(request);
            return response.StatusCode;
        }
        static async Task<HttpStatusCode> BlobUpload(string digest, string blobfile)
        {
            HttpResponseMessage response = await client.GetAsync(
                $"api/blobs/{digest}");
            return response.StatusCode;
        }
        static async Task RunBlobUpload(string digest, string blobfile, long btvalue)
        {
            try
            {
                var statusCode = (int)await BlobHead(digest);

                if (statusCode == 200)
                {
                    Console.WriteLine($"skipping upload for already created layer {digest}");
                }
                else if (statusCode == 404)
                {
                    Console.WriteLine($"uploading layer {digest}");

                    using (FileStream f = new FileStream(blobfile, FileMode.Open, FileAccess.Read))
                    {
                        Stopwatch stopwatch = new Stopwatch();

                        try
                        {
                            SetCursorVisible(false);

                            Stream originalFileStream = f;
                            Stream destFileStream = new ThrottledStream(originalFileStream, btvalue);

                            int block_size = 1024;
                            int total_size = (int)(new FileInfo(blobfile).Length/block_size);
                            var blob_size = ByteSize.FromKibiBytes(total_size);                            
                            var bar = new Tqdm.ProgressBar(total: (int)blob_size.MebiBytes, useColor: GetPlatformColor(), useExpMovingAvg: false, printsPerSecond: 10);
                            bool finished = false;
                            int tick = 0;
                            var streamcontent = new ProgressableStreamContent(new StreamContent(destFileStream), (sent, total) => {
                                tick++;
                                if (tick < 40) { return; }
                                tick = 0;
                                double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                                double speedInBytesPerSecond = sent / elapsedTimeInSeconds;
                                try
                                {
                                    int percentage = (int)((sent * 100.0) / total);
                                    var sent_size = ByteSize.FromKibiBytes(sent / block_size);
                                    percentage = (int)sent_size.MebiBytes;
                                    bar.SetLabel($"({ByteSize.FromKibiBytes(sent / block_size).ToString("#")} / {ByteSize.FromKibiBytes(total / block_size).ToString("#")}) uploading at {ByteSize.FromKibiBytes(speedInBytesPerSecond / block_size).ToString("#")}/s");
                                    if (!finished)
                                    {
                                        bar.Progress(percentage);
                                    }
                                    else
                                    {
                                        bar.Progress((int)blob_size.MebiBytes);
                                    }
                                }
                                catch
                                {
                                    // Silently ignore progress bar errors (console handle issues)
                                }

                            });

                            stopwatch.Start();
                            var response = await client.PostAsync($"api/blobs/{digest}", streamcontent);
                            finished = true;

                            SetCursorVisible(true);

                            if (response.IsSuccessStatusCode)
                            {
                                bar.Finish();
                                Console.WriteLine("success uploading layer");
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                            {
                                Console.WriteLine("Error: upload failed invalid digest, check both ollama are running the same version.");
                                System.Environment.Exit(1);
                            }
                            else
                            {
                                Console.WriteLine($"Error: upload failed: {response.ReasonPhrase}");
                                System.Environment.Exit(1);
                            }

                            streamcontent.Dispose();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error: {e.Message}");
                            SetCursorVisible(true);
                            System.Environment.Exit(1);
                        }
                        finally
                        {
                            stopwatch.Stop();
                        }
                    }

                }
                else
                {
                    Console.WriteLine($"Error: the remote server has answered with HTTP status code: {statusCode} for layer {digest}");
                    System.Environment.Exit(1);
                }

            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: error during blob upload: {e.Message}");
            }
        }

        static async Task RunCreateModel(string Modelname, Dictionary<string, string> files, string template = null, string system = null, List<string> parameters = null)
        {
            int exitcode = 0;
            try
            {
                var modelCreate = new Dictionary<string, object>
                {
                    { "model", Modelname },
                    { "files", files }
                };

                if (!string.IsNullOrEmpty(template))
                    modelCreate["template"] = template;
                if (!string.IsNullOrEmpty(system))
                    modelCreate["system"] = system;
                if (parameters != null && parameters.Count > 0)
                {
                    var paramDict = new Dictionary<string, string>();
                    foreach (var param in parameters)
                    {
                        var parts = param.Split(new[] { ' ' }, 2);
                        if (parts.Length == 2)
                            paramDict[parts[0]] = parts[1];
                    }
                    if (paramDict.Count > 0)
                        modelCreate["parameters"] = paramDict;
                }

                string data = System.Text.Json.JsonSerializer.Serialize(modelCreate);

                SetCursorVisible(false);

                try
                {
                    var body = new StringContent(data, Encoding.UTF8, "application/json");
                    var postmessage = new HttpRequestMessage(HttpMethod.Post, $"api/create");
                    postmessage.Content = body;
                    var response = await client.SendAsync(postmessage, HttpCompletionOption.ResponseHeadersRead);
                    var stream = await response.Content.ReadAsStreamAsync();
                    string laststatus = "";
                    using (var streamReader = new StreamReader(stream))
                    {
                        while (!streamReader.EndOfStream)
                        {
                            string? statusline = await streamReader.ReadLineAsync();
                            if (statusline != null)
                            {
                                RootStatus status = StatusReader.Read<RootStatus>(statusline);
                                if (status?.status != null)
                                {
                                    laststatus = status.status;
                                    Console.WriteLine(status.status);
                                }
                            }
                        }
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Error: could not create '{Modelname}' on the remote server (HTTP status {(int)response.StatusCode}): {response.ReasonPhrase}");
                        exitcode = 1;
                    }
                    else if (laststatus != "success")
                    {
                        exitcode = 1;
                    }
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"HttpRequestException: could not create '{Modelname}' on the remote server: {e.Message}");
                    exitcode = 1;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: could not create '{Modelname}' on the remote server: {e.Message}");
                exitcode = 1;
            }
            finally
            {
                SetCursorVisible(true);
                System.Environment.Exit(exitcode);
            }
        }
        public static string GetPlatformPath(string inputPath, string separator)
        {
            if (inputPath != "*")
            {
                return inputPath;
            }
            else
            {
                string thisOs = System.Environment.OSVersion.Platform.ToString();

                if (thisOs == "Win32NT")
                {
                    return $"{System.Environment.GetEnvironmentVariable("USERPROFILE")}{separator}.ollama{separator}models";
                }
                else if (thisOs == "Unix" && System.Environment.OSVersion.VersionString.Contains("Darwin"))
                {
                    return $"~{separator}.ollama{separator}models";
                }
                else
                {
                    return $"{separator}usr{separator}share{separator}ollama{separator}.ollama{separator}models";
                }
            }
        }
        
        public static string ModelBase(string modelName)
        {
            string[] parts = modelName.Split('/');

            if (modelName.Contains("/"))
            {
                return parts[0];
            }
            else
            {
                return "";
            }
        }

        public void ActionCopy(string Source, string Destination)
        {
            Init();

            Debug.WriteLine("Copy: you entered model '{0}' and server '{1}'. OLLAMA_MODELS={2}", Source, Destination, ollama_models);

            // Check if destination is a URL or a local model name
            bool isRemoteCopy = Destination.StartsWith("http://") || Destination.StartsWith("https://");

            if (isRemoteCopy)
            {
                // Remote copy
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }

                if (!ValidateServer(Destination))
                {
                    System.Environment.Exit(1);
                }

                ActionCopyRemote(Source, Destination);
            }
            else
            {
                // Local copy (destination is a model name)
                ActionCopyLocal(Source, Destination);
            }
        }

        private void ActionCopyLocal(string source, string destination)
        {
            // Apply :latest tag if not specified
            string sourceModel = source;
            string destModel = destination;

            if (!sourceModel.Contains(":"))
            {
                sourceModel = $"{sourceModel}:latest";
            }

            if (!destModel.Contains(":"))
            {
                destModel = $"{destModel}:latest";
            }

            // Check if destination already exists
            var checkProcess = new Process();
            checkProcess.StartInfo.FileName = "ollama";
            checkProcess.StartInfo.Arguments = "list";
            checkProcess.StartInfo.CreateNoWindow = true;
            checkProcess.StartInfo.UseShellExecute = false;
            checkProcess.StartInfo.RedirectStandardOutput = true;
            checkProcess.StartInfo.RedirectStandardError = true;

            try
            {
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd();
                checkProcess.WaitForExit();

                if (output.Contains(destModel))
                {
                    Console.WriteLine($"Error: destination model '{destModel}' already exists");
                    Console.WriteLine("Please choose a different name or remove the existing model first.");
                    System.Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: could not check if destination exists: {e.Message}");
            }

            Console.WriteLine($"Copying model '{sourceModel}' to '{destModel}'...");

            // Copy using ollama cp
            var copyProcess = new Process();
            copyProcess.StartInfo.FileName = "ollama";
            copyProcess.StartInfo.Arguments = $"cp {sourceModel} {destModel}";
            copyProcess.StartInfo.CreateNoWindow = true;
            copyProcess.StartInfo.UseShellExecute = false;
            copyProcess.StartInfo.RedirectStandardOutput = true;
            copyProcess.StartInfo.RedirectStandardError = true;

            try
            {
                copyProcess.Start();
                copyProcess.WaitForExit();

                if (copyProcess.ExitCode != 0)
                {
                    string error = copyProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Error: failed to copy model: {error}");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully copied '{sourceModel}' to '{destModel}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to copy model: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        private void ActionCopyRemote(string Source, string Destination)
        {
            if (!Directory.Exists(ollama_models))
            {
                Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                System.Environment.Exit(1);
            }

            if (!ValidateServer(Destination))
            {
                System.Environment.Exit(1);
            }

            string modelBase = ModelBase(Source);

            string modelDir;
            string blobDir = $"{ollama_models}{separator}blobs";
            string manifest_file = Source;
            if (!Source.Contains(":"))
            {
                manifest_file = $"{manifest_file}{separator}latest";
            }
            else
            {
                manifest_file = Source.Replace(":", separator);
            }

            if (modelBase == "hub")
            {
                modelDir = Path.Combine(ollama_models, "manifests", manifest_file);
            }
            else if (modelBase == "")
            {
                modelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", "library", manifest_file);
            }
            else
            {
                modelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", manifest_file);
            }

            if (!System.IO.File.Exists(modelDir))
            {
                // If model not found and no tag specified, try with :latest
                if (!Source.Contains(":"))
                {
                    string latestSource = $"{Source}:latest";
                    string latestManifestFile = latestSource.Replace(":", separator);
                    string latestModelDir;

                    if (modelBase == "hub")
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", latestManifestFile);
                    }
                    else if (modelBase == "")
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", "library", latestManifestFile);
                    }
                    else
                    {
                        latestModelDir = Path.Combine(ollama_models, "manifests", "registry.ollama.ai", latestManifestFile);
                    }

                    if (System.IO.File.Exists(latestModelDir))
                    {
                        // Found with :latest tag, update variables
                        Source = latestSource;
                        manifest_file = latestManifestFile;
                        modelDir = latestModelDir;
                    }
                    else
                    {
                        Console.WriteLine($"Error: model '{Source}' not found (tried '{Source}' and '{latestSource}')");
                        System.Environment.Exit(1);
                    }
                }
                else
                {
                    Console.WriteLine($"Error: model '{Source}' not found at: {modelDir}");
                    System.Environment.Exit(1);
                }
            }

            Console.WriteLine($"Copying model '{Source}' to '{Destination}'...");

            // Parse modelfile to extract template, system, and parameters
            var templateBuilder = new StringBuilder();
            var parameters = new List<string>();
            string system = null;
            bool inTemplate = false;

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = @" show " + Source + " --modelfile";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;

            var modelfileLines = new List<string>();
            p.OutputDataReceived += (a, b) => {
                if (b != null && b.Data != null)
                {
                    if (!b.Data.StartsWith("failed to get console mode"))
                    {
                        modelfileLines.Add(b.Data);
                    }
                }
            };

            var stdOutput = new StringBuilder();
            p.OutputDataReceived += (sender, args) => stdOutput.AppendLine(args.Data);

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.WaitForExit();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: get Modelfile from ollama show failed: {e.Message}");
                System.Environment.Exit(1);
            }

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"Error: get Modelfile from ollama show failed: {stdOutput.ToString()}");
                System.Environment.Exit(1);
            }

            stdOutput.Clear();
            stdOutput = null;

            // Parse the modelfile lines
            for (int i = 0; i < modelfileLines.Count; i++)
            {
                string line = modelfileLines[i];

                if (line.StartsWith("#"))
                    continue;

                if (line.StartsWith("TEMPLATE "))
                {
                    inTemplate = true;
                    string templateStart = line.Substring(9).TrimStart('"');
                    templateBuilder.AppendLine(templateStart);
                    continue;
                }

                if (inTemplate)
                {
                    if (line.EndsWith("\""))
                    {
                        templateBuilder.Append(line.TrimEnd('"'));
                        inTemplate = false;
                    }
                    else
                    {
                        templateBuilder.AppendLine(line);
                    }
                    continue;
                }

                if (line.StartsWith("SYSTEM "))
                {
                    system = line.Substring(7).Trim().Trim('"');
                    continue;
                }

                if (line.StartsWith("PARAMETER "))
                {
                    parameters.Add(line.Substring(10));
                    continue;
                }
            }

            string? template = templateBuilder.Length > 0 ? templateBuilder.ToString() : null;

            RootManifest manifest = ManifestReader.Read<RootManifest>(modelDir);

            // Build files dictionary and upload blobs
            var files = new Dictionary<string, string>();
            int modelIndex = 0;
            int adapterIndex = 0;
            int projectorIndex = 0;

            foreach (Layer layer in manifest.layers)
            {
                if (layer.mediaType.StartsWith("application/vnd.ollama.image.model"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue).GetAwaiter().GetResult();

                    string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                    files[filename] = digest;
                    modelIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.projector"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue).GetAwaiter().GetResult();

                    string filename = projectorIndex == 0 ? "projector.gguf" : $"projector_{projectorIndex}.gguf";
                    files[filename] = digest;
                    projectorIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.adapter"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue).GetAwaiter().GetResult();

                    string filename = adapterIndex == 0 ? "adapter.gguf" : $"adapter_{adapterIndex}.gguf";
                    files[filename] = digest;
                    adapterIndex++;
                }
            }

            Debug.WriteLine($"Creating model with files: {string.Join(", ", files.Keys)}");

            RunCreateModel(Source, files, template, system, parameters).GetAwaiter().GetResult();
        }
        public void ActionList(string Pattern, string Destination, string sortMode = "name")
        {
            Init();

            // If pattern looks like a URL, treat it as destination
            if (!string.IsNullOrEmpty(Pattern) && (Pattern.StartsWith("http://") || Pattern.StartsWith("https://")))
            {
                Destination = Pattern;
                Pattern = null;
            }

            Debug.WriteLine("List: you entered pattern '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Pattern, Destination, ollama_models);

            bool localList = string.IsNullOrEmpty(Destination);

            if (localList)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                ListLocalModels(Pattern, sortMode);
            }
            else
            {
                if (!ValidateServer(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                ListRemoteModels(Pattern, sortMode).GetAwaiter().GetResult();
            }
        }

        private string WildcardToRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return ".*";
            }
            string escaped = Regex.Escape(pattern);
            string regexPattern = "^" + escaped.Replace("\\*", ".*") + "$";
            return regexPattern;
        }

        private bool MatchesPattern(string modelName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
            {
                return true;
            }
            string regexPattern = WildcardToRegex(pattern);
            return Regex.IsMatch(modelName, regexPattern, RegexOptions.IgnoreCase);
        }

        private void ListLocalModels(string pattern, string sortMode = "name")
        {
            var models = new List<LocalModelInfo>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
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
                                // Official library models: just "model:tag"
                                fullModelName = $"{model}:{tag}";
                            }
                            else if (host == "registry.ollama.ai")
                            {
                                // User namespace models: "namespace/model:tag"
                                fullModelName = $"{ns}/{model}:{tag}";
                            }
                            else
                            {
                                // Other registries: "host/namespace/model:tag"
                                fullModelName = $"{host}/{ns}/{model}:{tag}";
                            }

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                var fileInfo = new FileInfo(tagFile);
                                long totalSize = 0;
                                string modelId = "";

                                try
                                {
                                    RootManifest manifest = ManifestReader.Read<RootManifest>(tagFile);
                                    if (manifest?.layers != null)
                                    {
                                        totalSize = manifest.layers.Sum(l => l.size);
                                    }
                                    if (manifest?.config?.digest != null)
                                    {
                                        string digest = manifest.config.digest;
                                        if (digest.StartsWith("sha256:"))
                                        {
                                            modelId = digest.Substring(7, 12);
                                        }
                                    }
                                }
                                catch { }

                                models.Add(new LocalModelInfo
                                {
                                    Name = fullModelName,
                                    Id = modelId,
                                    Size = totalSize,
                                    ModifiedAt = fileInfo.LastWriteTime
                                });
                            }
                        }
                    }
                }
            }

            if (models.Count == 0)
            {
                Console.WriteLine($"No models found matching pattern: {pattern ?? "*"}");
                return;
            }

            PrintModelTable(SortModels(models, sortMode));
        }

        private List<LocalModelInfo> SortModels(List<LocalModelInfo> models, string sortMode)
        {
            return sortMode switch
            {
                "size_desc" => models.OrderByDescending(m => m.Size).ToList(),
                "size_asc" => models.OrderBy(m => m.Size).ToList(),
                "time_desc" => models.OrderByDescending(m => m.ModifiedAt).ToList(),
                "time_asc" => models.OrderBy(m => m.ModifiedAt).ToList(),
                _ => models.OrderBy(m => m.Name).ToList()
            };
        }

        private async Task ListRemoteModels(string pattern, string sortMode = "name")
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

                if (modelsResponse?.models == null || modelsResponse.models.Count == 0)
                {
                    Console.WriteLine("No models found on remote server.");
                    return;
                }

                var filteredModels = modelsResponse.models
                    .Where(m => MatchesPattern(m.name, pattern))
                    .Select(m => new LocalModelInfo
                    {
                        Name = m.name,
                        Id = m.digest?.StartsWith("sha256:") == true ? m.digest.Substring(7, 12) : m.digest?.Substring(0, Math.Min(12, m.digest.Length)) ?? "",
                        Size = m.size,
                        ModifiedAt = m.modified_at
                    })
                    .ToList();

                if (filteredModels.Count == 0)
                {
                    Console.WriteLine($"No models found matching pattern: {pattern ?? "*"}");
                    return;
                }

                PrintModelTable(SortModels(filteredModels, sortMode));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: failed to list remote models: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        private void PrintModelTable(List<LocalModelInfo> models)
        {
            int nameWidth = Math.Max(50, models.Max(m => m.Name.Length) + 2);
            int idWidth = 16;
            int sizeWidth = 10;

            System.Console.WriteLine($"{"NAME".PadRight(nameWidth)}{"ID".PadRight(idWidth)}{"SIZE".PadRight(sizeWidth)}MODIFIED");

            foreach (var model in models)
            {
                var size = ByteSize.FromBytes(model.Size);
                string sizeStr = size.GigaBytes >= 1 ? $"{size.GigaBytes:F0} GB" : $"{size.MegaBytes:F0} MB";
                string timeAgo = GetTimeAgo(model.ModifiedAt);
                System.Console.WriteLine($"{model.Name.PadRight(nameWidth)}{model.Id.PadRight(idWidth)}{sizeStr.PadRight(sizeWidth)}{timeAgo}");
            }
        }

        private string GetTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.Now - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minutes ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hours ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)timeSpan.TotalDays} days ago";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} months ago";
            return $"{(int)(timeSpan.TotalDays / 365)} years ago";
        }

        public void ActionRemove(string Pattern, string Destination)
        {
            Init();

            // If pattern looks like a URL, treat it as destination
            if (!string.IsNullOrEmpty(Pattern) && (Pattern.StartsWith("http://") || Pattern.StartsWith("https://")))
            {
                Destination = Pattern;
                Pattern = null;
            }

            if (string.IsNullOrEmpty(Pattern))
            {
                Console.WriteLine("Error: pattern is required for remove command");
                System.Environment.Exit(1);
            }

            Debug.WriteLine("Remove: you entered pattern '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Pattern, Destination, ollama_models);

            bool localRemove = string.IsNullOrEmpty(Destination);

            if (localRemove)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                RemoveLocalModels(Pattern);
            }
            else
            {
                if (!ValidateServer(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                RemoveRemoteModels(Pattern).GetAwaiter().GetResult();
            }
        }

        private void RemoveLocalModels(string pattern)
        {
            var modelsToRemove = new List<string>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
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

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                modelsToRemove.Add(fullModelName);
                            }
                        }
                    }
                }
            }

            if (modelsToRemove.Count == 0)
            {
                // If no models found, pattern has no wildcards, and no tag specified, try with :latest
                if (!pattern.Contains("*") && !pattern.Contains(":"))
                {
                    string latestPattern = $"{pattern}:latest";

                    // Retry scan with :latest pattern
                    foreach (string hostDir in Directory.GetDirectories(manifestsDir))
                    {
                        string host = Path.GetFileName(hostDir);
                        foreach (string namespaceDir in Directory.GetDirectories(hostDir))
                        {
                            string ns = Path.GetFileName(namespaceDir);
                            foreach (string modelDir in Directory.GetDirectories(namespaceDir))
                            {
                                string model = Path.GetFileName(modelDir);
                                foreach (string tagFile in Directory.GetFiles(modelDir))
                                {
                                    string tag = Path.GetFileName(tagFile);
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

                                    if (MatchesPattern(fullModelName, latestPattern))
                                    {
                                        modelsToRemove.Add(fullModelName);
                                    }
                                }
                            }
                        }
                    }

                    if (modelsToRemove.Count == 0)
                    {
                        Console.WriteLine($"No models found matching pattern: {pattern} (tried '{pattern}' and '{latestPattern}')");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"No models found matching pattern: {pattern}");
                    return;
                }
            }

            Console.WriteLine($"Removing {modelsToRemove.Count} model(s)...");

            foreach (var modelName in modelsToRemove)
            {
                try
                {
                    // Use ollama rm command to properly remove the model
                    var p = new Process();
                    p.StartInfo.FileName = "ollama";
                    p.StartInfo.Arguments = $"rm {modelName}";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.StartInfo.RedirectStandardOutput = true;
                    p.StartInfo.RedirectStandardError = true;

                    p.Start();
                    p.WaitForExit();

                    if (p.ExitCode == 0)
                    {
                        Console.WriteLine($"deleted '{modelName}'");
                    }
                    else
                    {
                        string error = p.StandardError.ReadToEnd();
                        Console.WriteLine($"failed to delete '{modelName}': {error}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error deleting '{modelName}': {e.Message}");
                }
            }
        }

        private async Task RemoveRemoteModels(string pattern)
        {
            try
            {
                // First get the list of models
                HttpResponseMessage response = await client.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OllamaModelsResponse>(json);

                if (modelsResponse?.models == null || modelsResponse.models.Count == 0)
                {
                    Console.WriteLine("No models found on remote server.");
                    return;
                }

                var modelsToRemove = modelsResponse.models
                    .Where(m => MatchesPattern(m.name, pattern))
                    .Select(m => m.name)
                    .ToList();

                if (modelsToRemove.Count == 0)
                {
                    // If no models found, pattern has no wildcards, and no tag specified, try with :latest
                    if (!pattern.Contains("*") && !pattern.Contains(":"))
                    {
                        string latestPattern = $"{pattern}:latest";
                        modelsToRemove = modelsResponse.models
                            .Where(m => MatchesPattern(m.name, latestPattern))
                            .Select(m => m.name)
                            .ToList();

                        if (modelsToRemove.Count == 0)
                        {
                            Console.WriteLine($"No models found matching pattern: {pattern} (tried '{pattern}' and '{latestPattern}')");
                            return;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"No models found matching pattern: {pattern}");
                        return;
                    }
                }

                Console.WriteLine($"Removing {modelsToRemove.Count} model(s) from remote server...");

                foreach (var modelName in modelsToRemove)
                {
                    try
                    {
                        var deleteRequest = new { model = modelName };
                        string deleteJson = JsonSerializer.Serialize(deleteRequest);
                        var content = new StringContent(deleteJson, Encoding.UTF8, "application/json");

                        var request = new HttpRequestMessage(HttpMethod.Delete, "api/delete");
                        request.Content = content;

                        var deleteResponse = await client.SendAsync(request);

                        if (deleteResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"deleted '{modelName}'");
                        }
                        else if (deleteResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                        {
                            Console.WriteLine($"model '{modelName}' not found");
                        }
                        else
                        {
                            Console.WriteLine($"failed to delete '{modelName}': HTTP {(int)deleteResponse.StatusCode}");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error deleting '{modelName}': {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: failed to remove remote models: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        public void ActionRename(string source, string newName)
        {
            Init();

            // Apply :latest tag if not specified
            string sourceModel = source;
            string targetModel = newName;

            if (!sourceModel.Contains(":"))
            {
                sourceModel = $"{sourceModel}:latest";
            }

            if (!targetModel.Contains(":"))
            {
                targetModel = $"{targetModel}:latest";
            }

            Debug.WriteLine("Rename: source '{0}' to '{1}'", sourceModel, targetModel);

            // Check if destination already exists
            var checkProcess = new Process();
            checkProcess.StartInfo.FileName = "ollama";
            checkProcess.StartInfo.Arguments = "list";
            checkProcess.StartInfo.CreateNoWindow = true;
            checkProcess.StartInfo.UseShellExecute = false;
            checkProcess.StartInfo.RedirectStandardOutput = true;
            checkProcess.StartInfo.RedirectStandardError = true;

            try
            {
                checkProcess.Start();
                string output = checkProcess.StandardOutput.ReadToEnd();
                checkProcess.WaitForExit();

                if (output.Contains(targetModel))
                {
                    Console.WriteLine($"Error: destination model '{targetModel}' already exists");
                    Console.WriteLine("Please choose a different name or remove the existing model first.");
                    System.Environment.Exit(1);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: could not check if destination exists: {e.Message}");
            }

            Console.WriteLine($"Renaming model '{sourceModel}' to '{targetModel}'...");

            // Step 1: Copy the model using ollama cp
            Console.WriteLine($"Step 1/3: Copying '{sourceModel}' to '{targetModel}'...");
            var copyProcess = new Process();
            copyProcess.StartInfo.FileName = "ollama";
            copyProcess.StartInfo.Arguments = $"cp {sourceModel} {targetModel}";
            copyProcess.StartInfo.CreateNoWindow = true;
            copyProcess.StartInfo.UseShellExecute = false;
            copyProcess.StartInfo.RedirectStandardOutput = true;
            copyProcess.StartInfo.RedirectStandardError = true;

            try
            {
                copyProcess.Start();
                copyProcess.WaitForExit();

                if (copyProcess.ExitCode != 0)
                {
                    string error = copyProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Error: failed to copy model: {error}");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully copied to '{targetModel}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to copy model: {e.Message}");
                System.Environment.Exit(1);
            }

            // Step 2: Verify the new model exists
            Console.WriteLine($"Step 2/3: Verifying '{targetModel}' exists...");
            var listProcess = new Process();
            listProcess.StartInfo.FileName = "ollama";
            listProcess.StartInfo.Arguments = "list";
            listProcess.StartInfo.CreateNoWindow = true;
            listProcess.StartInfo.UseShellExecute = false;
            listProcess.StartInfo.RedirectStandardOutput = true;
            listProcess.StartInfo.RedirectStandardError = true;

            bool targetExists = false;
            try
            {
                listProcess.Start();
                string output = listProcess.StandardOutput.ReadToEnd();
                listProcess.WaitForExit();

                // Check if target model appears in the list
                targetExists = output.Contains(targetModel);

                if (!targetExists)
                {
                    Console.WriteLine($"Error: failed to verify '{targetModel}' - model not found after copy");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Verified: '{targetModel}' exists");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to verify model: {e.Message}");
                System.Environment.Exit(1);
            }

            // Step 3: Delete the original model
            Console.WriteLine($"Step 3/3: Deleting original '{sourceModel}'...");
            var deleteProcess = new Process();
            deleteProcess.StartInfo.FileName = "ollama";
            deleteProcess.StartInfo.Arguments = $"rm {sourceModel}";
            deleteProcess.StartInfo.CreateNoWindow = true;
            deleteProcess.StartInfo.UseShellExecute = false;
            deleteProcess.StartInfo.RedirectStandardOutput = true;
            deleteProcess.StartInfo.RedirectStandardError = true;

            try
            {
                deleteProcess.Start();
                deleteProcess.WaitForExit();

                if (deleteProcess.ExitCode != 0)
                {
                    string error = deleteProcess.StandardError.ReadToEnd();
                    Console.WriteLine($"Warning: failed to delete original model: {error}");
                    Console.WriteLine($"The model was successfully copied to '{targetModel}', but the original '{sourceModel}' still exists.");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Successfully deleted '{sourceModel}'");
                Console.WriteLine($"\nRename complete: '{source}' → '{newName}'");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: failed to delete original model: {e.Message}");
                Console.WriteLine($"The model was successfully copied to '{targetModel}', but the original '{sourceModel}' still exists.");
                System.Environment.Exit(1);
            }
        }

        public void ActionUpdate(string Pattern, string Destination)
        {
            Init();

            // If pattern looks like a URL, treat it as destination and update all models
            if (!string.IsNullOrEmpty(Pattern) && (Pattern.StartsWith("http://") || Pattern.StartsWith("https://")))
            {
                Destination = Pattern;
                Pattern = "*";
            }

            // If pattern is null or empty, default to updating all models
            if (string.IsNullOrEmpty(Pattern))
            {
                Pattern = "*";
            }

            Debug.WriteLine("Update: pattern '{0}', destination '{1}'", Pattern, Destination);

            bool localUpdate = string.IsNullOrEmpty(Destination);

            if (localUpdate)
            {
                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }
                UpdateLocalModels(Pattern);
            }
            else
            {
                if (!ValidateServer(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                UpdateRemoteModels(Pattern, Destination).GetAwaiter().GetResult();
            }
        }

        private void UpdateLocalModels(string pattern)
        {
            var modelsToUpdate = new List<string>();
            string manifestsDir = Path.Combine(ollama_models, "manifests");

            if (!Directory.Exists(manifestsDir))
            {
                Console.WriteLine($"No local models found.");
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

                            if (MatchesPattern(fullModelName, pattern))
                            {
                                modelsToUpdate.Add(fullModelName);
                            }
                        }
                    }
                }
            }

            if (modelsToUpdate.Count == 0)
            {
                Console.WriteLine($"No models found matching pattern: {pattern}");
                return;
            }

            Console.WriteLine($"Updating {modelsToUpdate.Count} model(s)...\n");

            foreach (var modelName in modelsToUpdate)
            {
                UpdateSingleLocalModel(modelName);
            }
        }

        private void UpdateSingleLocalModel(string modelName)
        {
            try
            {
                Console.WriteLine($"Updating '{modelName}'...");

                var p = new Process();
                p.StartInfo.FileName = "ollama";
                p.StartInfo.Arguments = $"pull {modelName}";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                var output = new StringBuilder();
                var errorOutput = new StringBuilder();
                bool hadDownloadActivity = false;

                p.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        output.AppendLine(e.Data);
                        Console.WriteLine(e.Data);

                        // Check if there's actual download activity (pulling layers with progress)
                        if (e.Data.Contains("pulling") && (e.Data.Contains("%") || e.Data.Contains("B/")))
                        {
                            hadDownloadActivity = true;
                        }
                    }
                };

                p.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorOutput.AppendLine(e.Data);
                    }
                };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    string fullOutput = output.ToString().ToLower();

                    // Check various patterns that indicate model is already up to date
                    if (fullOutput.Contains("up to date") ||
                        fullOutput.Contains("already up-to-date") ||
                        fullOutput.Contains("already exists") ||
                        (!hadDownloadActivity && fullOutput.Contains("success")))
                    {
                        Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                    }
                    else if (hadDownloadActivity)
                    {
                        Console.WriteLine($"✓ '{modelName}' updated successfully\n");
                    }
                    else
                    {
                        // If no clear indicator, assume it's up to date (no downloads happened)
                        Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                    }
                }
                else
                {
                    string error = errorOutput.ToString();
                    Console.WriteLine($"✗ Failed to update '{modelName}': {error}\n");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"✗ Error updating '{modelName}': {e.Message}\n");
            }
        }

        private async Task UpdateRemoteModels(string pattern, string destination)
        {
            try
            {
                // Set the base address for the client
                var originalBaseAddress = client.BaseAddress;
                client.BaseAddress = new Uri(destination);

                // First get the list of models
                HttpResponseMessage response = await client.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
                    client.BaseAddress = originalBaseAddress;
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var models = doc.RootElement.GetProperty("models");

                var modelsToUpdate = new List<string>();
                foreach (var model in models.EnumerateArray())
                {
                    string modelName = model.GetProperty("name").GetString();
                    if (MatchesPattern(modelName, pattern))
                    {
                        modelsToUpdate.Add(modelName);
                    }
                }

                if (modelsToUpdate.Count == 0)
                {
                    Console.WriteLine($"No models found matching pattern: {pattern}");
                    client.BaseAddress = originalBaseAddress;
                    return;
                }

                Console.WriteLine($"Updating {modelsToUpdate.Count} model(s) on remote server...\n");

                foreach (var modelName in modelsToUpdate)
                {
                    await UpdateSingleRemoteModel(modelName, destination);
                }

                client.BaseAddress = originalBaseAddress;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                System.Environment.Exit(1);
            }
        }

        private async Task UpdateSingleRemoteModel(string modelName, string destination)
        {
            try
            {
                Console.WriteLine($"Updating '{modelName}' on remote server...");

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

                var response = await client.PostAsync("api/pull", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"✗ Failed to update '{modelName}': HTTP {(int)response.StatusCode}\n");
                    return;
                }

                // Read the streaming response
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                bool isUpToDate = false;
                bool hasError = false;
                bool hadDownloadActivity = false;
                string lastStatus = "";

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var lineDoc = JsonDocument.Parse(line);
                        var root = lineDoc.RootElement;

                        if (root.TryGetProperty("status", out var statusProp))
                        {
                            lastStatus = statusProp.GetString();
                            Console.WriteLine(lastStatus);

                            string statusLower = lastStatus.ToLower();

                            // Check for up-to-date messages
                            if (statusLower.Contains("up to date") ||
                                statusLower.Contains("already up-to-date") ||
                                statusLower.Contains("already exists"))
                            {
                                isUpToDate = true;
                            }

                            // Check for actual download activity
                            if (statusLower.Contains("pulling") || statusLower.Contains("downloading"))
                            {
                                // Check if there's a total/completed bytes indicator (actual download)
                                if (root.TryGetProperty("completed", out _) || root.TryGetProperty("total", out _))
                                {
                                    hadDownloadActivity = true;
                                }
                            }
                        }

                        if (root.TryGetProperty("error", out var errorProp))
                        {
                            hasError = true;
                            Console.WriteLine($"Error: {errorProp.GetString()}");
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON lines
                    }
                }

                if (hasError)
                {
                    Console.WriteLine($"✗ Failed to update '{modelName}'\n");
                }
                else if (isUpToDate || !hadDownloadActivity)
                {
                    Console.WriteLine($"✓ '{modelName}' is already up to date\n");
                }
                else
                {
                    Console.WriteLine($"✓ '{modelName}' updated successfully\n");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"✗ Error updating '{modelName}': {e.Message}\n");
            }
        }

        public void Init()
        {
            // Clear any tab completion options from the screen
            LocalModelsTabCompletionSource.ClearPreviousOptions();

            var env_models = System.Environment.GetEnvironmentVariable("OLLAMA_MODELS");
            if (env_models != null && env_models.Length > 0)
            {
                ollama_models = env_models;
            }
            if (env_models == null || env_models.Length < 1)
            {
                env_models = System.Environment.GetEnvironmentVariable("OLLAMA_MODELS", EnvironmentVariableTarget.User);
                if (env_models != null && env_models.Length > 0)
                {
                    ollama_models = env_models;
                }
            }
            if (ollama_models.Length < 1)
            {
                ollama_models = "*";
            }
            ollama_models = GetPlatformPath(ollama_models, separator);

            btvalue = ParseBandwidthThrottling(this.BandwidthThrottling);

        }
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"osync v{OsyncProgram.GetAppVersion()}");
                System.Console.WriteLine();
                Console.ResetColors();
                Args.InvokeAction<OsyncProgram>(args);
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine(ArgUsage.GenerateUsageFromTemplate<OsyncProgram>().ToString());
                System.Environment.Exit(1);
            }
        }
    }

    public static class Tools
    {
        public static bool IsNumeric(this string text) { long _out; return long.TryParse(text, out _out); }
    }

    public class LocalModelsTabCompletionSource : ITabCompletionSource
    {
        string[] models;
        private static int lastDisplayedLines = 0;

        public LocalModelsTabCompletionSource()
        {
            string model_re = "^(?<modelname>\\S*).*";
            var Modelsbuild = new StringBuilder();
            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = @" ls";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;
            p.OutputDataReceived += (a, b) =>
            {
                if (b != null && b.Data != null)
                {
                    if (!b.Data.StartsWith("failed to get console mode") &&
                        !b.Data.StartsWith("NAME"))
                    {
                        Match m = Regex.Match(b.Data, model_re, RegexOptions.IgnoreCase);
                        if (m.Groups["modelname"].Success)
                            Modelsbuild.AppendLine(m.Groups["modelname"].Value.ToString().Trim());
                    }
                }
            };
            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.WaitForExit();
            }
            catch (Exception e)
            {
                models = new string[] { "" };
            }

            models = Modelsbuild.ToString().Split('\n');
            Modelsbuild.Clear();
            Modelsbuild = null;
        }

        public static void ClearPreviousOptions()
        {
            if (lastDisplayedLines > 0)
            {
                try
                {
                    // The options are displayed BELOW the current line, so clear lines down from here
                    int currentLeft = System.Console.CursorLeft;
                    int currentTop = System.Console.CursorTop;

                    // Move down to the next line (where options start)
                    System.Console.SetCursorPosition(0, currentTop + 1);

                    // Clear each line of options
                    for (int i = 0; i < lastDisplayedLines; i++)
                    {
                        System.Console.Write(new string(' ', System.Console.WindowWidth));
                        if (i < lastDisplayedLines - 1)
                        {
                            System.Console.SetCursorPosition(0, System.Console.CursorTop + 1);
                        }
                    }

                    // Move cursor back to the original prompt position
                    System.Console.SetCursorPosition(currentLeft, currentTop);

                    lastDisplayedLines = 0;
                }
                catch
                {
                    // If clearing fails, just reset the counter
                    lastDisplayedLines = 0;
                }
            }
        }

        public bool TryComplete(TabCompletionContext context, out string? completion)
        {
            try
            {
                var match = from m in models where m.StartsWith(context.CompletionCandidate) select m;
                if (match.Count() == 1)
                {
                    // Clear options when completing to a single match
                    ClearPreviousOptions();
                    completion = match.Single().ToString().Trim();
                    return true;
                }
                else if (match.Count() > 1)
                {
                    // Find common prefix
                    var minimumLength = match.Min(x => x.Length);
                    int commonChars;
                    for (commonChars = 0; commonChars < minimumLength; commonChars++)
                    {
                        if (match.Select(x => x[commonChars]).Distinct().Count() > 1)
                        {
                            break;
                        }
                    }

                    string commonPrefix = match.First().ToString().Trim().Substring(0, commonChars);

                    // If user has typed to the common prefix, show all available options
                    if (commonChars == context.CompletionCandidate.Length)
                    {
                        // Display all available options
                        System.Console.WriteLine();
                        var sortedMatches = match.OrderBy(x => x).ToList();

                        foreach (var item in sortedMatches)
                        {
                            System.Console.WriteLine("  " + item);
                        }
                        System.Console.WriteLine();

                        // Track how many lines we displayed (1 blank + items + 1 blank)
                        lastDisplayedLines = sortedMatches.Count + 2;

                        // Return the common prefix, PowerArgs will redraw the prompt below the options
                        completion = commonPrefix;
                        return true;
                    }
                    else
                    {
                        // Clear options when completing further (not showing new ones)
                        if (lastDisplayedLines > 0)
                        {
                            ClearPreviousOptions();
                        }
                    }

                    if (match != null && match.Any())
                    {
                        completion = commonPrefix;
                        return true;
                    }
                    else
                    {
                        completion = null;
                        return false;
                    }
                }
                else
                {
                    completion = null;
                    return false;
                }
            }
            catch(Exception e)
            {
                completion = null;
                return false;
            }
        }
    }

    public static class ManifestReader
    {
        public static T Read<T>(string filePath)
        {
            try
            {
                string text = System.IO.File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<T>(text);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the manifest file: " + ex.Message);
                return default;
            }
        }
    }
    public static class StatusReader
    {
        public static T Read<T>(string statusline)
        {
            try
            {                
                return JsonSerializer.Deserialize<T>(statusline);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the status line: " + ex.Message);
                return default;
            }
        }
    }
    public static class VersionReader
    {
        public static T Read<T>(string versionline)
        {
            try
            {
                return JsonSerializer.Deserialize<T>(versionline);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error parsing the version: " + ex.Message);
                return default;
            }
        }
    }

    public class Config
    {
        public string mediaType { get; set; }
        public string digest { get; set; }
        public int size { get; set; }
    }

    public class Layer
    {
        public string mediaType { get; set; }
        public string digest { get; set; }
        public long size { get; set; }
        public string from { get; set; }
    }

    public class RootManifest
    {
        public int schemaVersion { get; set; }
        public string mediaType { get; set; }
        public Config config { get; set; }
        public List<Layer> layers { get; set; }
    }

    public class RootStatus
    {
        public string status { get; set; }
    }
    public class RootVersion
    {
        public string version { get; set; }
    }

    public class LocalModelInfo
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public long Size { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    public class OllamaModelsResponse
    {
        public List<OllamaModel> models { get; set; }
    }

    public class OllamaModel
    {
        public string name { get; set; }
        public string model { get; set; }
        public DateTime modified_at { get; set; }
        public long size { get; set; }
        public string digest { get; set; }
    }

    // From https://stackoverflow.com/a/41392145/4213397
    internal class ProgressableStreamContent : HttpContent
    {
        /// <summary>
        /// Lets keep buffer of 20kb
        /// </summary>
        private const int defaultBufferSize = 5 * 4096;

        private HttpContent content;

        private int bufferSize;

        //private bool contentConsumed;
        private Action<long, long> progress;

        public ProgressableStreamContent(HttpContent content, Action<long, long> progress) : this(content,
            defaultBufferSize, progress)
        {
        }

        public ProgressableStreamContent(HttpContent content, int bufferSize, Action<long, long> progress)
        {
            if (content == null)
            {
                throw new ArgumentNullException("content");
            }

            if (bufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException("bufferSize");
            }

            this.content = content;
            this.bufferSize = bufferSize;
            this.progress = progress;

            foreach (var h in content.Headers)
            {
                this.Headers.Add(h.Key, h.Value);
            }
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            return Task.Run(async () =>
            {
                var buffer = new Byte[this.bufferSize];
                long size;
                TryComputeLength(out size);
                long uploaded = 0;
                using (var sinput = await content.ReadAsStreamAsync())
                {
                    while (true)
                    {
                        var length = sinput.Read(buffer, 0, buffer.Length);
                        if (length <= 0) break;
                        //downloader.Uploaded = uploaded += length;
                        uploaded += length;
                        progress?.Invoke(uploaded, size);
                        //System.Diagnostics.Debug.WriteLine($"Bytes sent {uploaded} of {size}");
                        stream.Write(buffer, 0, length);
                        stream.Flush();
                    }
                }
                stream.Flush();
            });
        }

        protected override bool TryComputeLength(out long length)
        {
            length = content.Headers.ContentLength.GetValueOrDefault();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                content.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}
