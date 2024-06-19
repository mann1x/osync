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
using Console = PrettyConsole.Console;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.RegularExpressions;
using Windows.Devices.Power;
using Microsoft.VisualBasic;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace osync
{
    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling), TabCompletion(typeof(LocalModelsTabCompletionSource), HistoryToSave = 10, REPL = true)]
    public class OsyncProgram
    {
        static string version = "1.0.5";
        static HttpClient client = new HttpClient();
        public static string GetAppVersion()
        {
            return version;
        }

        [HelpHook, ArgShortcut("-?"), ArgShortcut("-h"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgRequired, ArgDescription("Source local model to copy eg: mistral:latest"), ArgPosition(0)]
        public string LocalModel { get; set; }

        [ArgRequired, ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgPosition(1)]
        public string RemoteServer { get; set; }

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
        public bool ValidateServer(string RemoteServer)
        {
            try
            {                
                
                Uri RemoteUri = new Uri(RemoteServer);
                if ((RemoteUri.Scheme == "http" || RemoteUri.Scheme == "https") == false) { return false; };
                
                client.BaseAddress = new Uri(RemoteServer);
                client.Timeout = TimeSpan.FromDays(24);

                RunCheckServer().GetAwaiter().GetResult();

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

        static async Task RunCheckServer()
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(
                    $"api/version");
                var statusCode = (int)response.StatusCode;

                if (statusCode >= 100 && statusCode < 400)
                {
                    string versionjson = response.Content.ReadAsStringAsync().Result;
                    RootVersion _version = VersionReader.Read<RootVersion>(versionjson);
                    Console.WriteLine();

                    Console.WriteLine($"The remote ollama server is active and running v{_version.version}");
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

                        Stream originalFileStream = f;
                        Stream destFileStream = new ThrottledStream(originalFileStream, btvalue);

                        int block_size = 1024;
                        int total_size = (int)(new FileInfo(blobfile).Length/block_size);
                        var arr = Enumerable.Range(0, 100).ToArray();
                        var bar = new Tqdm.ProgressBar(total: total_size, useColor: GetPlatformColor(), useExpMovingAvg: false);
                        bar.SetThemeAscii();
                        var streamcontent = new ProgressableStreamContent(new StreamContent(destFileStream), (sent, total) => {
                            double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                            double speedInBytesPerSecond = sent / elapsedTimeInSeconds;
                            string speed;
                            if (speedInBytesPerSecond < 1024)
                            {
                                speed = $"{(int)(speedInBytesPerSecond)} B/s";
                            }
                            else if (speedInBytesPerSecond < 1024 * 1024)
                            {
                                speed = $"{(int)(speedInBytesPerSecond / 1024)} KB/s";
                            }
                            else if (speedInBytesPerSecond < 1024 * 1024 * 1024)
                            {
                                speed = $"{(int)(speedInBytesPerSecond / (1024 * 1024))} MB/s";
                            }
                            else
                            {
                                speed = $"{(int)(speedInBytesPerSecond / (1024 * 1024 * 1024))} GB/s";
                            }
                            bar.SetLabel($"uploading at {speed}");
                            bar.Progress((int)(sent / block_size));
                        });

                        try
                        {
                            stopwatch.Start();
                            var response = await client.PostAsync($"api/blobs/{digest}", streamcontent);
                            bar.Progress(total_size);
                            bar.Finish();
                            stopwatch.Stop();

                            if (response.IsSuccessStatusCode)
                            {
                                Console.WriteLine("Success uploading layer.");
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
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error: {e.Message}");
                            System.Environment.Exit(1);
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

        static async Task RunCreateModel(string Modelname, string Modelfile)
        {
            try
            {
                var modelCreate = new
                {
                    name = Modelname,
                    modelfile = Modelfile
                };
                string data = System.Text.Json.JsonSerializer.Serialize(modelCreate);
                
                System.Console.CursorVisible = false;

                try
                {
                    var requestContent = new MultipartFormDataContent();
                    var body = new StringContent(data, Encoding.UTF8, "application/x-www-form-urlencoded");
                    requestContent.Add(body);
                    //Console.WriteLine($"{data}");
                    var postmessage = new HttpRequestMessage(HttpMethod.Post, $"api/create");
                    postmessage.Content = body;
                    var response = await client.SendAsync(postmessage, HttpCompletionOption.ResponseHeadersRead);
                    var stream = response.Content.ReadAsStreamAsync().Result;
                    using (var streamReader = new StreamReader(stream))
                    {
                        while (!streamReader.EndOfStream)
                        {
                            //Console.WriteLine(streamReader.ReadLine());
                            string? statusline = streamReader.ReadLine();
                            if (statusline != null)
                            {
                                RootStatus status = StatusReader.Read<RootStatus>(statusline);
                                Thread.Sleep(2000);
                                Console.WriteLine(status.status);
                            }
                        }
                    }
                    
                    response.EnsureSuccessStatusCode();

                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Error: could not create '{Modelname}' on the remote server (HTTP status {(int)response.StatusCode}): {response.ReasonPhrase}");
                    }
                    System.Console.CursorVisible = true;
                }
                catch (HttpRequestException e)
                {
                    Console.WriteLine($"HttpRequestException: could not create '{Modelname}' on the remote server: {e.Message}");
                    System.Console.CursorVisible = true;
                }


            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: could not create '{Modelname}' on the remote server: {e.Message}");
                System.Console.CursorVisible = true;
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

        public void Main()
        {
            string ollama_models = "";
            string separator = Path.DirectorySeparatorChar.ToString();

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
            
            Debug.WriteLine("You entered model '{0}' and server '{1}'. OLLAMA_MODELS={2}", this.LocalModel, this.RemoteServer, ollama_models);

            long btvalue = ParseBandwidthThrottling(this.BandwidthThrottling);

            if (!Directory.Exists(ollama_models))
            {
                Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                System.Environment.Exit(1);
            }

            if (!ValidateServer(this.RemoteServer))
            {
                System.Environment.Exit(1);
            }

            string modelBase = ModelBase(this.LocalModel);

            string modelDir;
            string blobDir = $"{ollama_models}{separator}blobs";
            string manifest_file = this.LocalModel.Replace(":", separator);

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
                Console.WriteLine($"Error: model '{this.LocalModel}' not found at: {modelDir}");
                System.Environment.Exit(1);
            }

            Console.WriteLine($"Copying model '{this.LocalModel}' to '{this.RemoteServer}'...");

            var Modelbuild = new StringBuilder();
            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = @" show " + this.LocalModel + " --modelfile";
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            p.StartInfo.RedirectStandardError = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardInput = false;
            p.OutputDataReceived += (a, b) => { 
                if(b != null && b.Data != null)
                {
                    if (!b.Data.StartsWith("failed to get console mode") &&
                        !b.Data.StartsWith("#") &&
                        !b.Data.StartsWith("FROM "))
                    {
                        Modelbuild.AppendLine(b.Data);
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

            RootManifest manifest = ManifestReader.Read<RootManifest>(modelDir);

            foreach (Layer layer in manifest.layers)
            {
                if (layer.mediaType.StartsWith("application/vnd.ollama.image.model") ||
                    layer.mediaType.StartsWith("application/vnd.ollama.image.projector") ||
                    layer.mediaType.StartsWith("application/vnd.ollama.image.adapter"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue).GetAwaiter().GetResult();
                    Modelbuild.Insert(0, $"FROM @{digest}" + System.Environment.NewLine);
                }
            }


            string Modelfile = Modelbuild.ToString();
            Debug.WriteLine("Modelfile:\n{0}", Modelfile);

            Modelbuild.Clear();
            Modelbuild = null;

            RunCreateModel(LocalModel, Modelfile).GetAwaiter().GetResult();

        }
    }
    internal class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"osync v{OsyncProgram.GetAppVersion()}");
                Args.InvokeMain<OsyncProgram>(args);
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

        public bool TryComplete(TabCompletionContext context, out string? completion)
        {
            try
            {
                var match = from m in models where m.StartsWith(context.CompletionCandidate) select m;
                if (match.Count() == 1)
                {
                    completion = match.Single().ToString().Trim();
                    return true;
                }
                else if (match.Count() > 1)
                {
                    var minimumLength = match.Min(x => x.Length);
                    int commonChars;
                    for (commonChars = 0; commonChars < minimumLength; commonChars++)
                    {
                        if (match.Select(x => x[commonChars]).Distinct().Count() > 1)
                        {
                            break;
                        }
                    }

                    if (match != null && match.Any())
                    {
                        completion = match.First().ToString().Trim().Substring(0, commonChars);
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
