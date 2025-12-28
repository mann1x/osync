// Ignore Spelling: osync

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
        [ArgRequired, ArgDescription("Source: local model name (e.g., mistral:latest) or remote server URL with model (e.g., http://192.168.0.100:11434/mistral:latest)"), ArgPosition(1)]
        public string Source { get; set; }
        [ArgRequired, ArgDescription("Destination: local model name (e.g., my-model:v1) or remote server URL with model (e.g., http://192.168.0.100:11434/my-model:v1). Note: Remote-to-remote copy only works for models originally from the Ollama registry. Locally created models cannot be copied between remote servers."), ArgExample("my-backup-model", "local model name"), ArgExample("http://192.168.0.100:11434/mistral:latest", "remote server with model"), ArgPosition(2)]
        public string Destination { get; set; }
        [ArgDescription("Buffer size for remote-to-remote copy (e.g., 256MB, 1GB). Default: 512MB")]
        public string BufferSize { get; set; }
    }

    public class ListArgs
    {
        [ArgDescription("Model pattern, supports * as wildcard, eg: mistral:* or *mistral*"),
         ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d")]
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
        [ArgDescription("Model pattern to delete, supports * as wildcard, eg: mistral:* or *mistral*"),
         ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d")]
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
        [ArgDescription("Model pattern to update, supports * as wildcard (e.g., llama* or * for all models). If omitted, updates all models."),
         ArgPosition(1)]
        public string Pattern { get; set; }
        [ArgDescription("Remote ollama server, eg: http://192.168.0.100:11434"), ArgExample("http://192.168.0.100:11434", "protocol://ip:port"), ArgShortcut("-d")]
        public string Destination { get; set; }
    }

    public class PullArgs
    {
        [ArgRequired,
         ArgDescription("Model name to pull from registry. Supports standard format (llama3, llama3:7b), HuggingFace short format (hf.co/user/repo:tag), or full HuggingFace URL"),
         ArgExample("llama3", "standard model"),
         ArgExample("llama3:7b", "model with tag"),
         ArgExample("hf.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF:IQ2_M", "HuggingFace short format"),
         ArgExample("https://huggingface.co/bartowski/Qwen2.5.1-Coder-7B-Instruct-GGUF/blob/main/Qwen2.5.1-Coder-7B-Instruct-IQ2_M.gguf", "HuggingFace URL"),
         ArgPosition(1)]
        public string ModelName { get; set; }
        [ArgDescription("Remote ollama server URL for remote pull (optional). If omitted, pulls locally."),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d")]
        public string Destination { get; set; }
    }

    public class ShowArgs
    {
        [ArgRequired,
         ArgDescription("Model name to show information about"),
         ArgExample("llama3", "show basic info"),
         ArgExample("llama3:7b", "show info with tag"),
         ArgPosition(1)]
        public string ModelName { get; set; }

        [ArgDescription("Remote ollama server URL for remote show (optional). If omitted, shows locally."),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d")]
        public string Destination { get; set; }

        [ArgDescription("Show license of the model"),
         ArgShortcut("--license")]
        public bool License { get; set; }

        [ArgDescription("Show Modelfile of the model"),
         ArgShortcut("--modelfile")]
        public bool Modelfile { get; set; }

        [ArgDescription("Show parameters of the model"),
         ArgShortcut("--parameters")]
        public bool Parameters { get; set; }

        [ArgDescription("Show system message of the model"),
         ArgShortcut("--system")]
        public bool System { get; set; }

        [ArgDescription("Show template of the model"),
         ArgShortcut("--template")]
        public bool Template { get; set; }

        [ArgDescription("Show detailed model information"),
         ArgShortcut("-v"),
         ArgShortcut("--verbose")]
        public bool Verbose { get; set; }
    }

    public class RunArgs
    {
        [ArgRequired,
         ArgDescription("Model name to chat with"),
         ArgExample("llama3.2:1b", "basic model"),
         ArgExample("deepseek-r1:1.5b", "thinking model"),
         ArgPosition(1)]
        public string ModelName { get; set; }

        [ArgDescription("Ollama server URL (default: OLLAMA_HOST env var or http://localhost:11434)"),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d")]
        public string Destination { get; set; }

        [ArgDescription("Response format (e.g. json)"),
         ArgShortcut("--format")]
        public string Format { get; set; }

        [ArgDescription("Duration to keep model loaded (e.g. 5m, 1h)"),
         ArgShortcut("--keepalive")]
        public string KeepAlive { get; set; }

        [ArgDescription("Don't wrap words to next line automatically"),
         ArgShortcut("--nowordwrap")]
        public bool NoWordWrap { get; set; }

        [ArgDescription("Show timings for response"),
         ArgShortcut("--verbose")]
        public bool Verbose { get; set; }

        [ArgDescription("Truncate output embeddings to specified dimension"),
         ArgShortcut("--dimensions")]
        public int? Dimensions { get; set; }

        [ArgDescription("Hide thinking output (if provided)"),
         ArgShortcut("--hidethinking")]
        public bool HideThinking { get; set; }

        [ArgDescription("Use an insecure registry"),
         ArgShortcut("--insecure")]
        public bool Insecure { get; set; }

        [ArgDescription("Enable thinking mode: true/false or high/medium/low"),
         ArgShortcut("--think")]
        public string Think { get; set; }

        [ArgDescription("Truncate inputs exceeding context length (default: true)"),
         ArgShortcut("--truncate")]
        public bool? Truncate { get; set; }
    }

    public class PsArgs
    {
        [ArgDescription("Ollama server URL (default: OLLAMA_HOST env var or http://localhost:11434)"),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d"),
         ArgPosition(1)]
        public string Destination { get; set; }
    }

    public class LoadArgs
    {
        [ArgRequired,
         ArgDescription("Model name to load into memory"),
         ArgExample("llama3", "basic model"),
         ArgExample("mistral-nemo", "larger model"),
         ArgPosition(1)]
        public string ModelName { get; set; }

        [ArgDescription("Ollama server URL (default: OLLAMA_HOST env var or http://localhost:11434)"),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d")]
        public string Destination { get; set; }
    }

    public class UnloadArgs
    {
        [ArgDescription("Model name to unload from memory (optional - if not specified, unloads all models)"),
         ArgExample("llama3", "specific model"),
         ArgPosition(1)]
        public string ModelName { get; set; }

        [ArgDescription("Ollama server URL (default: OLLAMA_HOST env var or http://localhost:11434)"),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d")]
        public string Destination { get; set; }
    }

    public class ManageArgs
    {
        [ArgDescription("Remote ollama server (optional - defaults to local)"),
         ArgExample("http://192.168.0.100:11434", "remote server"),
         ArgShortcut("-d"),
         ArgPosition(1)]
        public string Destination { get; set; }
    }

    // Progress wrapper stream that displays transfer progress
    public class ProgressStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private readonly ByteSize _totalSize;
        private long _bytesTransferred;

        public ProgressStream(Stream baseStream, long totalBytes, ByteSize totalSize)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _totalSize = totalSize;
            _bytesTransferred = 0;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position
        {
            get => _bytesTransferred;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _bytesTransferred += bytesRead;

            // Show progress
            int percentage = _totalBytes > 0 ? (int)((_bytesTransferred * 100) / _totalBytes) : 0;
            var transferred = ByteSize.FromBytes(_bytesTransferred);
            Console.Write($"\r  Progress: {percentage}% ({transferred.ToString("#.##")} / {_totalSize.ToString("#.##")})");

            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesTransferred += bytesRead;

            // Show progress
            int percentage = _totalBytes > 0 ? (int)((_bytesTransferred * 100) / _totalBytes) : 0;
            var transferred = ByteSize.FromBytes(_bytesTransferred);
            Console.Write($"\r  Progress: {percentage}% ({transferred.ToString("#.##")} / {_totalSize.ToString("#.##")})");

            return bytesRead;
        }

        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _baseStream?.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Bounded buffer stream for simultaneous download/upload with backpressure
    public class BufferedPipeStream : Stream
    {
        private readonly SemaphoreSlim _writeSemaphore;
        private readonly SemaphoreSlim _readSemaphore;
        private readonly Queue<byte[]> _bufferQueue;
        private readonly object _lock = new object();
        private readonly long _maxBufferSize;
        private long _currentBufferSize;
        private bool _writeCompleted;
        private Exception _exception;

        public BufferedPipeStream(long maxBufferSize)
        {
            _maxBufferSize = maxBufferSize;
            _bufferQueue = new Queue<byte[]>();
            _writeSemaphore = new SemaphoreSlim(1, 1);
            _readSemaphore = new SemaphoreSlim(0);
            _currentBufferSize = 0;
            _writeCompleted = false;
        }

        public void CompleteWriting()
        {
            lock (_lock)
            {
                _writeCompleted = true;
                _readSemaphore.Release();
            }
        }

        public void SetException(Exception ex)
        {
            lock (_lock)
            {
                _exception = ex;
                _readSemaphore.Release();
            }
        }

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_exception != null)
                throw _exception;

            // Create a copy of the data to write
            byte[] data = new byte[count];
            Array.Copy(buffer, offset, data, 0, count);

            // Wait if buffer is full (backpressure)
            while (true)
            {
                lock (_lock)
                {
                    if (_currentBufferSize + count <= _maxBufferSize)
                    {
                        _bufferQueue.Enqueue(data);
                        _currentBufferSize += count;
                        _readSemaphore.Release();
                        return;
                    }
                }

                // Buffer is full, wait a bit before retrying (backpressure)
                await Task.Delay(10, cancellationToken);
            }
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            while (true)
            {
                if (_exception != null)
                    throw _exception;

                lock (_lock)
                {
                    if (_bufferQueue.Count > 0)
                    {
                        byte[] data = _bufferQueue.Dequeue();
                        int bytesToCopy = Math.Min(data.Length, count);
                        Array.Copy(data, 0, buffer, offset, bytesToCopy);
                        _currentBufferSize -= data.Length;

                        // If we didn't copy all data, put the rest back
                        if (bytesToCopy < data.Length)
                        {
                            byte[] remaining = new byte[data.Length - bytesToCopy];
                            Array.Copy(data, bytesToCopy, remaining, 0, remaining.Length);
                            var tempQueue = new Queue<byte[]>();
                            tempQueue.Enqueue(remaining);
                            while (_bufferQueue.Count > 0)
                                tempQueue.Enqueue(_bufferQueue.Dequeue());
                            _bufferQueue.Clear();
                            while (tempQueue.Count > 0)
                                _bufferQueue.Enqueue(tempQueue.Dequeue());
                            _currentBufferSize += remaining.Length;
                        }

                        return bytesToCopy;
                    }
                    else if (_writeCompleted)
                    {
                        return 0; // End of stream
                    }
                }

                // Wait for data to be available
                await _readSemaphore.WaitAsync(cancellationToken);
            }
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => WriteAsync(buffer, offset, count).GetAwaiter().GetResult();
    }

    // Stream wrapper for tracking upload progress
    public class ProgressTrackingStream : Stream
    {
        private readonly Stream _baseStream;
        private readonly long _totalBytes;
        private long _bytesRead;
        private readonly Action<long> _progressCallback;

        public ProgressTrackingStream(Stream baseStream, long totalBytes, Action<long> progressCallback)
        {
            _baseStream = baseStream;
            _totalBytes = totalBytes;
            _progressCallback = progressCallback;
            _bytesRead = 0;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _baseStream.ReadAsync(buffer, offset, count, cancellationToken);
            _bytesRead += bytesRead;
            _progressCallback?.Invoke(_bytesRead);
            return bytesRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _baseStream.Read(buffer, offset, count);
            _bytesRead += bytesRead;
            _progressCallback?.Invoke(_bytesRead);
            return bytesRead;
        }

        public override bool CanRead => _baseStream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _totalBytes;
        public override long Position { get => _bytesRead; set => throw new NotSupportedException(); }
        public override void Flush() => _baseStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling), TabCompletion(typeof(LocalModelsTabCompletionSource), HistoryToSave = 10, REPL = true)]
    public class OsyncProgram
    {
        static string version = "1.1.6";
        static HttpClient client = new HttpClient() { Timeout = TimeSpan.FromDays(1) };
        public static bool isInteractiveMode = false;
        public string ollama_models = "";
        long btvalue = 0;
        string separator = Path.DirectorySeparatorChar.ToString();

        // Flag to indicate we're running from manage mode - errors should throw instead of Exit
        internal bool ThrowOnError { get; set; } = false;

        public static string GetAppVersion()
        {
            return version;
        }

        [HelpHook, ArgShortcut("-?"), ArgShortcut("-h"), ArgDescription("Shows this help")]
        public bool Help { get; set; }

        [ArgActionMethod, ArgDescription("Copy a model locally or to a remote server (alias: cp)"), ArgShortcut("cp")]
        public void Copy(CopyArgs args)
        {
            ActionCopy(args.Source, args.Destination, args.BufferSize);
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
            if (string.IsNullOrWhiteSpace(args.Pattern))
            {
                Console.WriteLine("Error: Model pattern is required");
                Console.WriteLine("Usage: osync rm <model-pattern> [-d <server-url>]");
                System.Environment.Exit(1);
            }
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

        [ArgActionMethod, ArgDescription("Pull (download) a model from the registry locally or to a remote server")]
        public void Pull(PullArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync pull <model-name> [-d <server-url>]");
                System.Environment.Exit(1);
            }
            ActionPull(args.ModelName, args.Destination);
        }

        [ArgActionMethod, ArgDescription("Show information about a model locally or on a remote server")]
        public void Show(ShowArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync show <model-name> [-d <server-url>] [options]");
                System.Environment.Exit(1);
            }
            ActionShow(args.ModelName, args.Destination, args.License, args.Modelfile, args.Parameters, args.System, args.Template, args.Verbose);
        }

        [ArgActionMethod, ArgDescription("Run an interactive chat session with a model (alias: chat)"), ArgShortcut("chat")]
        public async Task Run(RunArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync run <model-name> [-d <server-url>] [options]");
                System.Environment.Exit(1);
            }

            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Ensure the URL has a protocol
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                ollamaHost = "http://" + ollamaHost;
            }

            // Ensure model has a tag
            var modelName = args.ModelName;
            if (!modelName.Contains(':'))
            {
                modelName += ":latest";
            }

            try
            {
                var session = new ChatSession(ollamaHost, modelName, args);
                await session.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Show running models and their status")]
        public async Task Ps(PsArgs args)
        {
            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Ensure the URL has a protocol
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                ollamaHost = "http://" + ollamaHost;
            }

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                var response = await httpClient.GetAsync("/api/ps");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                if (status?.Models == null || status.Models.Count == 0)
                {
                    Console.WriteLine("No models currently loaded in memory");
                    return;
                }

                Console.WriteLine();
                Console.WriteLine("Loaded Models:");
                Console.WriteLine(new string('-', 135));
                Console.WriteLine($"{"NAME",-30} {"ID",-15} {"SIZE",-25} {"VRAM USAGE",-15} {"CONTEXT",-10} {"UNTIL",-30}");
                Console.WriteLine(new string('-', 135));

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

                    Console.WriteLine($"{name,-30} {id,-15} {size,-25} {vramUsage,-15} {context,-10} {until,-30}");
                }

                Console.WriteLine(new string('-', 135));
                Console.WriteLine();
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                Console.WriteLine($"Details: {ex.Message}");
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
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

        private string FormatModelSize(long sizeBytes, string parameterSize)
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

        [ArgActionMethod, ArgDescription("Load a model into memory")]
        public async Task Load(LoadArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.ModelName))
            {
                Console.WriteLine("Error: Model name is required");
                Console.WriteLine("Usage: osync load <model-name> [-d <server-url>]");
                System.Environment.Exit(1);
            }

            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Ensure the URL has a protocol
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                ollamaHost = "http://" + ollamaHost;
            }

            // Ensure model has a tag
            var modelName = args.ModelName;
            if (!modelName.Contains(':'))
            {
                modelName += ":latest";
            }

            try
            {
                Console.WriteLine($"Loading model '{modelName}' into memory...");

                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                // Send empty chat request to preload the model
                var preloadRequest = new
                {
                    model = modelName
                };

                var jsonContent = JsonSerializer.Serialize(preloadRequest);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                {
                    Content = httpContent
                };

                var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // Consume the response to complete the preload
                await response.Content.ReadAsStringAsync();

                Console.WriteLine($"✓ Model '{modelName}' loaded successfully");
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                Console.WriteLine($"Details: {ex.Message}");
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Unload a model (or all models) from memory")]
        public async Task Unload(UnloadArgs args)
        {
            // Get Ollama host from environment variable or argument
            var ollamaHost = args.Destination
                ?? System.Environment.GetEnvironmentVariable("OLLAMA_HOST")
                ?? "http://localhost:11434";

            // If OLLAMA_HOST is 0.0.0.0 (bind address), replace with localhost
            if (ollamaHost == "0.0.0.0" || ollamaHost == "0.0.0.0:11434")
            {
                ollamaHost = "http://localhost:11434";
            }

            // Ensure the URL has a protocol
            if (!ollamaHost.StartsWith("http://") && !ollamaHost.StartsWith("https://"))
            {
                ollamaHost = "http://" + ollamaHost;
            }

            try
            {
                using var httpClient = new HttpClient
                {
                    BaseAddress = new Uri(ollamaHost),
                    Timeout = Timeout.InfiniteTimeSpan
                };

                List<string> modelsToUnload = new List<string>();

                // If model name is specified, unload just that model
                if (!string.IsNullOrWhiteSpace(args.ModelName))
                {
                    var modelName = args.ModelName;
                    if (!modelName.Contains(':'))
                    {
                        modelName += ":latest";
                    }
                    modelsToUnload.Add(modelName);
                }
                else
                {
                    // Get all loaded models from /api/ps
                    Console.WriteLine("Fetching loaded models...");
                    var psResponse = await httpClient.GetAsync("/api/ps");
                    psResponse.EnsureSuccessStatusCode();

                    var json = await psResponse.Content.ReadAsStringAsync();
                    var status = JsonSerializer.Deserialize<ProcessStatusResponse>(json);

                    if (status?.Models == null || status.Models.Count == 0)
                    {
                        Console.WriteLine("No models currently loaded in memory");
                        return;
                    }

                    modelsToUnload.AddRange(status.Models.Select(m => m.Name));
                }

                // Unload each model
                foreach (var modelName in modelsToUnload)
                {
                    Console.WriteLine($"Unloading model '{modelName}'...");

                    var unloadRequest = new
                    {
                        model = modelName,
                        keep_alive = 0
                    };

                    var jsonContent = JsonSerializer.Serialize(unloadRequest);
                    var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    var requestMessage = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
                    {
                        Content = httpContent
                    };

                    var response = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    // Consume the response
                    await response.Content.ReadAsStringAsync();

                    Console.WriteLine($"✓ Model '{modelName}' unloaded successfully");
                }

                if (modelsToUnload.Count > 1)
                {
                    Console.WriteLine($"\nUnloaded {modelsToUnload.Count} models");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"Error: Could not connect to Ollama server at {ollamaHost}");
                Console.WriteLine($"Details: {ex.Message}");
                System.Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner error: {ex.InnerException.Message}");
                }
                System.Environment.Exit(1);
            }
        }

        [ArgActionMethod, ArgDescription("Interactive TUI for model management")]
        public void Manage(ManageArgs args)
        {
            Init();

            // Validate destination if provided
            if (!string.IsNullOrEmpty(args.Destination))
            {
                if (!ValidateServerUrl(args.Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
            }

            // Launch TUI
            var manageUI = new ManageUI(this, args.Destination);
            manageUI.Run();
        }

        [ArgActionMethod, ArgDescription("Clear the console screen (interactive mode only)")]
        public void Clear()
        {
            if (!isInteractiveMode)
            {
                Console.WriteLine("Error: The 'clear' command is only available in interactive mode.");
                Console.WriteLine("Start interactive mode by running 'osync' without arguments.");
                System.Environment.Exit(1);
            }

            Console.Clear();
            Console.WriteLine($"osync v{GetAppVersion()}");
            Console.WriteLine();
        }

        [ArgActionMethod, ArgDescription("Exit interactive mode"), ArgShortcut("exit")]
        public void Quit()
        {
            // PowerArgs REPL mode will handle this by exiting when Environment.Exit is called
            // Note: Due to PowerArgs limitations, Ctrl+D may show a character on Windows - use 'quit' or 'exit' instead
            System.Environment.Exit(0);
        }

        [ArgActionMethod, ArgDescription("Install osync to user directory, add to PATH, and optionally configure shell completion")]
        public void Install()
        {
            ActionInstall();
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

        private bool ValidateServerUrl(string serverUrl, bool silent = false)
        {
            try
            {
                Uri serverUri = new Uri(serverUrl);
                if (serverUri.Scheme != "http" && serverUri.Scheme != "https")
                {
                    return false;
                }

                // Create a temporary client for validation to avoid modifying global client state
                using (var tempClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(10) })
                {
                    tempClient.BaseAddress = new Uri(serverUrl);
                    var response = tempClient.GetAsync("api/version").GetAwaiter().GetResult();
                    var statusCode = (int)response.StatusCode;

                    if (statusCode >= 100 && statusCode < 400)
                    {
                        return true;
                    }
                    else if (statusCode >= 500 && statusCode <= 510)
                    {
                        if (!silent)
                            Console.WriteLine($"Error: the server {serverUrl} has thrown an internal error. ollama instance is not available");
                        return false;
                    }
                    else
                    {
                        if (!silent)
                            Console.WriteLine($"Error: the server {serverUrl} answered with HTTP status code: {statusCode}");
                        return false;
                    }
                }
            }
            catch (UriFormatException)
            {
                if (!silent)
                    Console.WriteLine($"Error: server URL is not valid: {serverUrl}");
                return false;
            }
            catch (Exception ex)
            {
                if (!silent)
                    Console.WriteLine($"Could not connect to server {serverUrl}: {ex.Message}");
                return false;
            }
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
        static async Task<HttpStatusCode> BlobHead(string digest, string? serverUrl = null)
        {
            HttpRequestMessage request;
            if (!string.IsNullOrEmpty(serverUrl))
            {
                request = new(HttpMethod.Head, $"{serverUrl.TrimEnd('/')}/api/blobs/{digest}");
            }
            else
            {
                request = new(HttpMethod.Head, $"api/blobs/{digest}");
            }
            HttpResponseMessage response = await client.SendAsync(request);
            return response.StatusCode;
        }
        static async Task<HttpStatusCode> BlobUpload(string digest, string blobfile)
        {
            HttpResponseMessage response = await client.GetAsync(
                $"api/blobs/{digest}");
            return response.StatusCode;
        }
        static async Task RunBlobUpload(string digest, string blobfile, long btvalue, CancellationToken cancellationToken = default, bool throwOnError = false, string? serverUrl = null)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var statusCode = (int)await BlobHead(digest, serverUrl);
                cancellationToken.ThrowIfCancellationRequested();

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
                            string postUrl = !string.IsNullOrEmpty(serverUrl)
                                ? $"{serverUrl.TrimEnd('/')}/api/blobs/{digest}"
                                : $"api/blobs/{digest}";
                            var response = await client.PostAsync(postUrl, streamcontent, cancellationToken);
                            finished = true;

                            SetCursorVisible(true);

                            if (response.IsSuccessStatusCode)
                            {
                                bar.Finish();
                                Console.WriteLine("success uploading layer");
                            }
                            else if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                            {
                                var errorMsg = "Error: upload failed invalid digest, check both ollama are running the same version.";
                                Console.WriteLine(errorMsg);
                                if (throwOnError) throw new Exception(errorMsg);
                                System.Environment.Exit(1);
                            }
                            else
                            {
                                var errorMsg = $"Error: upload failed: {response.ReasonPhrase}";
                                Console.WriteLine(errorMsg);
                                if (throwOnError) throw new Exception(errorMsg);
                                System.Environment.Exit(1);
                            }

                            streamcontent.Dispose();
                        }
                        catch (Exception e) when (e is OperationCanceledException || e is TaskCanceledException)
                        {
                            SetCursorVisible(true);
                            throw; // Re-throw cancellation to allow proper handling
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Error: {e.Message}");
                            SetCursorVisible(true);
                            if (throwOnError) throw;
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
                    var errorMsg = $"Error: the remote server has answered with HTTP status code: {statusCode} for layer {digest}";
                    Console.WriteLine(errorMsg);
                    if (throwOnError) throw new Exception(errorMsg);
                    System.Environment.Exit(1);
                }

            }
            catch (Exception e) when (e is OperationCanceledException || e is TaskCanceledException)
            {
                throw; // Re-throw cancellation
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception: error during blob upload: {e.Message}");
                if (throwOnError) throw;
            }
        }

        static async Task RunCreateModel(string Modelname, Dictionary<string, string> files, string? template = null, string? system = null, List<string>? parameters = null, string? serverUrl = null, bool throwOnError = false)
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
                    string createUrl = !string.IsNullOrEmpty(serverUrl)
                        ? $"{serverUrl.TrimEnd('/')}/api/create"
                        : $"api/create";
                    var postmessage = new HttpRequestMessage(HttpMethod.Post, createUrl);
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
                // Only exit if not in manage mode (throwOnError=false means we should exit on error)
                if (!throwOnError)
                {
                    System.Environment.Exit(exitcode);
                }
                else if (exitcode != 0)
                {
                    // In manage mode, throw exception instead of exiting
                    throw new Exception($"Blob upload failed with exit code {exitcode}");
                }
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

        private (string serverUrl, string modelName) ParseRemoteSource(string source)
        {
            // Format: http://server:port/modelname:tag or http://server:port/namespace/modelname:tag
            try
            {
                Uri uri = new Uri(source);
                string serverUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                string modelName = uri.AbsolutePath.TrimStart('/');

                if (string.IsNullOrEmpty(modelName))
                {
                    Console.WriteLine("Error: model name must be specified in the URL (e.g., http://server:port/modelname:tag)");
                    System.Environment.Exit(1);
                }

                return (serverUrl, modelName);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing remote URL: {ex.Message}");
                System.Environment.Exit(1);
                return (null, null);
            }
        }

        public void ActionCopy(string Source, string Destination, string? BufferSize = null, CancellationToken cancellationToken = default)
        {
            Init();

            Debug.WriteLine("Copy: you entered model '{0}' and destination '{1}'. OLLAMA_MODELS={2}", Source, Destination, ollama_models);

            // Check for cancellation before starting
            cancellationToken.ThrowIfCancellationRequested();

            // Detect if source and destination are remote or local
            bool isSourceRemote = Source.StartsWith("http://") || Source.StartsWith("https://");
            bool isDestinationRemote = Destination.StartsWith("http://") || Destination.StartsWith("https://");

            if (isSourceRemote && isDestinationRemote)
            {
                // Remote to Remote copy - use streaming buffer
                var (sourceServer, sourceModel) = ParseRemoteSource(Source);
                var (destServer, destModel) = ParseRemoteSource(Destination);

                Console.WriteLine($"Copying '{sourceModel}' from {sourceServer} to '{destModel}' on {destServer}...");
                ActionCopyRemoteToRemoteStreaming(sourceServer, sourceModel, destServer, destModel, BufferSize).GetAwaiter().GetResult();
            }
            else if (isSourceRemote && !isDestinationRemote)
            {
                // Remote to Local copy
                var (sourceServer, sourceModel) = ParseRemoteSource(Source);

                Console.WriteLine($"Copying '{sourceModel}' from {sourceServer} to local '{Destination}'...");
                ActionCopyRemoteToLocal(sourceServer, sourceModel, Destination);
            }
            else if (!isSourceRemote && isDestinationRemote)
            {
                // Local to Remote copy
                var (destServer, destModel) = ParseRemoteSource(Destination);

                if (!Directory.Exists(ollama_models))
                {
                    Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                    System.Environment.Exit(1);
                }

                if (!ValidateServerUrl(destServer))
                {
                    System.Environment.Exit(1);
                }

                ActionCopyLocalToRemote(Source, destServer, destModel, cancellationToken);
            }
            else
            {
                // Local to Local copy
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

        private void ActionCopyLocalToRemote(string Source, string destServer, string destModel, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(ollama_models))
            {
                Console.WriteLine($"Error: ollama models directory not found at: {ollama_models}");
                System.Environment.Exit(1);
            }

            if (!ValidateServerUrl(destServer))
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

            Console.WriteLine($"Copying model '{Source}' to '{destModel}' on {destServer}...");

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
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                    files[filename] = digest;
                    modelIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.projector"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = projectorIndex == 0 ? "projector.gguf" : $"projector_{projectorIndex}.gguf";
                    files[filename] = digest;
                    projectorIndex++;
                }
                else if (layer.mediaType.StartsWith("application/vnd.ollama.image.adapter"))
                {
                    var digest = layer.digest;
                    var hash = digest.Substring(7);
                    var blobfile = $"{blobDir}{separator}sha256-{hash}";
                    RunBlobUpload(digest, blobfile, btvalue, cancellationToken, ThrowOnError, destServer).GetAwaiter().GetResult();

                    string filename = adapterIndex == 0 ? "adapter.gguf" : $"adapter_{adapterIndex}.gguf";
                    files[filename] = digest;
                    adapterIndex++;
                }
            }

            Debug.WriteLine($"Creating model with files: {string.Join(", ", files.Keys)}");

            RunCreateModel(destModel, files, template, system, parameters, destServer, ThrowOnError).GetAwaiter().GetResult();
        }

        private async Task ActionCopyRemoteToRemoteStreaming(string sourceServer, string sourceModel, string destServer, string destModel, string? bufferSizeStr)
        {
            try
            {
                // Validate both servers without modifying global client state
                if (!ValidateServerUrl(sourceServer))
                {
                    Console.WriteLine($"Error: cannot connect to source server {sourceServer}");
                    System.Environment.Exit(1);
                }

                if (!ValidateServerUrl(destServer))
                {
                    Console.WriteLine($"Error: cannot connect to destination server {destServer}");
                    System.Environment.Exit(1);
                }

                // Parse buffer size (default 512MB)
                long bufferSize = 512 * 1024 * 1024; // 512MB default
                if (!string.IsNullOrEmpty(bufferSizeStr))
                {
                    bufferSize = ParseSize(bufferSizeStr);
                }
                Console.WriteLine($"Using buffer size: {ByteSize.FromBytes(bufferSize).ToString("#.##")}");

                // Create dedicated client for source server
                var sourceClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                sourceClient.BaseAddress = new Uri(sourceServer);

                Console.WriteLine($"Fetching model information from source server...");

                // Get modelfile string
                var (modelfile, modelfileErr) = await GetRemoteModelfileStringWithClient(sourceClient, sourceModel);

                if (modelfile == null)
                {
                    Console.WriteLine($"Error: Could not retrieve model from source server");
                    if (modelfileErr != null)
                    {
                        Console.WriteLine($"Details: {modelfileErr}");
                    }
                    System.Environment.Exit(1);
                }

                // Extract blob digests from modelfile
                var blobDigests = ExtractBlobDigestsFromModelfile(modelfile);

                if (blobDigests.Count == 0)
                {
                    Console.WriteLine($"Error: No blob references found in modelfile");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"Found {blobDigests.Count} blob(s) to transfer");

                // Get modelfile information for creating model on destination
                var (template, system, parameters) = await GetRemoteModelfileWithClient(sourceClient, sourceModel);

                // Stream each blob from source to destination through memory buffer
                var files = new Dictionary<string, string>();
                int modelIndex = 0;

                foreach (var digest in blobDigests)
                {
                    Console.WriteLine($"\nTransferring blob {digest.Substring(0, 19)}...");

                    await StreamBlobSourceToDestination(
                        sourceServer,
                        destServer,
                        sourceModel,
                        digest,
                        bufferSize
                    );

                    // Track files for model creation
                    string filename = modelIndex == 0 ? "model.gguf" : $"model_{modelIndex}.gguf";
                    files[filename] = digest;
                    modelIndex++;
                }

                // Create model on destination server
                Console.WriteLine($"\nCreating model '{destModel}' on destination server...");
                var destClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                destClient.BaseAddress = new Uri(destServer);

                await RunCreateModelWithClient(destClient, destModel, files, template, system, parameters);

                Console.WriteLine($"\nSuccessfully copied '{sourceModel}' from {sourceServer} to '{destModel}' on {destServer}");

                sourceClient.Dispose();
                destClient.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError during copy: {ex.Message}");
                throw;
            }
        }

        private void ActionCopyRemoteToLocal(string sourceServer, string sourceModel, string destModel)
        {
            // Validate source server
            if (!ValidateServerUrl(sourceServer))
            {
                Console.WriteLine($"Error: cannot connect to source server {sourceServer}");
                System.Environment.Exit(1);
            }

            var originalBaseAddress = client.BaseAddress;

            try
            {
                // Get model from source using ollama pull API
                client.BaseAddress = new Uri(sourceServer);
                Console.WriteLine($"Pulling model '{sourceModel}' from source server...");

                // Use ollama pull to download from remote and then copy locally
                var pullProcess = new Process();
                pullProcess.StartInfo.FileName = "ollama";
                pullProcess.StartInfo.Arguments = $"pull {sourceModel}";
                pullProcess.StartInfo.CreateNoWindow = true;
                pullProcess.StartInfo.UseShellExecute = false;
                pullProcess.StartInfo.RedirectStandardOutput = true;
                pullProcess.StartInfo.RedirectStandardError = true;
                pullProcess.StartInfo.EnvironmentVariables["OLLAMA_HOST"] = sourceServer;

                try
                {
                    pullProcess.Start();
                    string output = pullProcess.StandardOutput.ReadToEnd();
                    string error = pullProcess.StandardError.ReadToEnd();
                    pullProcess.WaitForExit();

                    if (pullProcess.ExitCode != 0)
                    {
                        Console.WriteLine($"Error: failed to pull model from source server: {error}");
                        System.Environment.Exit(1);
                    }

                    Console.WriteLine(output);
                    Console.WriteLine($"Successfully pulled '{sourceModel}' from source server");

                    // Now copy locally if destination name is different
                    if (sourceModel != destModel)
                    {
                        Console.WriteLine($"Copying to '{destModel}'...");
                        ActionCopyLocal(sourceModel, destModel);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception: failed to pull model: {e.Message}");
                    System.Environment.Exit(1);
                }
            }
            finally
            {
                client.BaseAddress = originalBaseAddress;
            }
        }

        private async Task<RootManifest> GetRemoteModelManifest(string modelName)
        {
            try
            {
                var showRequest = new { name = modelName, verbose = true };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                string json = await response.Content.ReadAsStringAsync();

                // Try to parse the response to see what we get
                try
                {
                    var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);

                    // Check if there's a model_info section with layers
                    if (showResponse.RootElement.TryGetProperty("model_info", out var modelInfo))
                    {
                        // Try to construct a manifest from model_info
                        var manifest = new RootManifest
                        {
                            schemaVersion = 2,
                            mediaType = "application/vnd.docker.distribution.manifest.v2+json",
                            layers = new List<Layer>()
                        };

                        // Extract layers if available
                        if (modelInfo.TryGetProperty("layers", out var layersElement))
                        {
                            foreach (var layer in layersElement.EnumerateArray())
                            {
                                var layerObj = new Layer
                                {
                                    mediaType = layer.TryGetProperty("media_type", out var mt) ? mt.GetString() : "application/vnd.ollama.image.model",
                                    digest = layer.TryGetProperty("digest", out var d) ? d.GetString() : "",
                                    size = layer.TryGetProperty("size", out var s) ? s.GetInt64() : 0
                                };
                                manifest.layers.Add(layerObj);
                            }
                        }

                        if (manifest.layers.Count > 0)
                        {
                            return manifest;
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    Console.WriteLine($"Error parsing show response: {parseEx.Message}");
                    Console.WriteLine($"Response: {json.Substring(0, Math.Min(500, json.Length))}...");
                }

                return null;
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting model manifest: {e.Message}");
                return null;
            }
        }

        private async Task<(string template, string system, List<string> parameters)> GetRemoteModelfile(string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await client.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);

                string template = null;
                string system = null;
                List<string> parameters = new List<string>();

                if (showResponse.RootElement.TryGetProperty("template", out var templateElement))
                {
                    template = templateElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("system", out var systemElement))
                {
                    system = systemElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("parameters", out var parametersElement))
                {
                    // Parameters can be either a string or an object
                    if (parametersElement.ValueKind == JsonValueKind.String)
                    {
                        // It's a string, parse it line by line
                        var paramStr = parametersElement.GetString();
                        if (!string.IsNullOrEmpty(paramStr))
                        {
                            var lines = paramStr.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    parameters.Add(line.Trim());
                                }
                            }
                        }
                    }
                    else if (parametersElement.ValueKind == JsonValueKind.Object)
                    {
                        // It's an object, enumerate properties
                        foreach (var param in parametersElement.EnumerateObject())
                        {
                            parameters.Add($"{param.Name} {param.Value.GetRawText()}");
                        }
                    }
                }

                return (template, system, parameters);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting modelfile: {e.Message}");
                return (null, null, null);
            }
        }

        private async Task<(string modelfile, string error)> GetRemoteModelfileString(string modelName)
        {
            return await GetRemoteModelfileStringWithClient(client, modelName);
        }

        private async Task<(string modelfile, string error)> GetRemoteModelfileStringWithClient(HttpClient httpClient, string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    return (null, error);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);

                // Get the modelfile string
                if (showResponse.RootElement.TryGetProperty("modelfile", out var modelfileElement))
                {
                    return (modelfileElement.GetString(), null);
                }

                return (null, "No modelfile found in response");
            }
            catch (Exception e)
            {
                return (null, e.Message);
            }
        }

        private async Task<(string template, string system, List<string> parameters)> GetRemoteModelfileWithClient(HttpClient httpClient, string modelName)
        {
            try
            {
                var showRequest = new { name = modelName };
                string showJson = JsonSerializer.Serialize(showRequest);
                var content = new StringContent(showJson, Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    return (null, null, null);
                }

                string json = await response.Content.ReadAsStringAsync();
                var showResponse = JsonSerializer.Deserialize<JsonDocument>(json);

                string template = null;
                string system = null;
                List<string> parameters = new List<string>();

                if (showResponse.RootElement.TryGetProperty("template", out var templateElement))
                {
                    template = templateElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("system", out var systemElement))
                {
                    system = systemElement.GetString();
                }

                if (showResponse.RootElement.TryGetProperty("parameters", out var parametersElement))
                {
                    // Parameters can be either a string or an object
                    if (parametersElement.ValueKind == JsonValueKind.String)
                    {
                        // It's a string, parse it line by line
                        var paramStr = parametersElement.GetString();
                        if (!string.IsNullOrEmpty(paramStr))
                        {
                            var lines = paramStr.Split('\n');
                            foreach (var line in lines)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    parameters.Add(line.Trim());
                                }
                            }
                        }
                    }
                    else if (parametersElement.ValueKind == JsonValueKind.Object)
                    {
                        // It's an object, enumerate properties
                        foreach (var param in parametersElement.EnumerateObject())
                        {
                            parameters.Add($"{param.Name} {param.Value.GetRawText()}");
                        }
                    }
                }

                return (template, system, parameters);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error getting modelfile: {e.Message}");
                return (null, null, null);
            }
        }

        private async Task RunCreateModelWithClient(HttpClient httpClient, string Modelname, Dictionary<string, string> files, string template = null, string system = null, List<string> parameters = null)
        {
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

                var body = new StringContent(data, Encoding.UTF8, "application/json");
                var postmessage = new HttpRequestMessage(HttpMethod.Post, $"api/create");
                postmessage.Content = body;
                var response = await httpClient.SendAsync(postmessage, HttpCompletionOption.ResponseHeadersRead);
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
                    throw new Exception($"Could not create '{Modelname}' on the remote server (HTTP status {(int)response.StatusCode}): {response.ReasonPhrase}");
                }
                else if (laststatus != "success")
                {
                    throw new Exception($"Model creation did not complete successfully (status: {laststatus})");
                }
            }
            catch (HttpRequestException e)
            {
                throw new Exception($"Could not create '{Modelname}' on the remote server: {e.Message}", e);
            }
        }

        private List<string> ExtractBlobDigestsFromModelfile(string modelfile)
        {
            var digests = new List<string>();

            if (string.IsNullOrEmpty(modelfile))
            {
                return digests;
            }

            // Parse the modelfile line by line
            var lines = modelfile.Split('\n');

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Look for FROM statements with blob paths
                // Format: FROM /path/to/blobs/sha256-<hash>
                if (trimmedLine.StartsWith("FROM "))
                {
                    var path = trimmedLine.Substring(5).Trim();

                    // Extract the sha256 digest from the path
                    // Path format: /usr/share/ollama/.ollama/models/blobs/sha256-<hash>
                    var blobMatch = Regex.Match(path, @"sha256-([a-f0-9]{64})");
                    if (blobMatch.Success)
                    {
                        string digest = $"sha256:{blobMatch.Groups[1].Value}";
                        if (!digests.Contains(digest))
                        {
                            digests.Add(digest);
                        }
                    }
                }
                // Also check for ADAPTER statements which may reference blobs
                else if (trimmedLine.StartsWith("ADAPTER "))
                {
                    var path = trimmedLine.Substring(8).Trim();

                    var blobMatch = Regex.Match(path, @"sha256-([a-f0-9]{64})");
                    if (blobMatch.Success)
                    {
                        string digest = $"sha256:{blobMatch.Groups[1].Value}";
                        if (!digests.Contains(digest))
                        {
                            digests.Add(digest);
                        }
                    }
                }
            }

            return digests;
        }

        private string TransformModelfileForLocalCreate(string modelfile)
        {
            if (string.IsNullOrEmpty(modelfile))
            {
                return modelfile;
            }

            // Replace file paths with blob digest references
            // Transform: FROM /path/to/blobs/sha256-<hash>
            // To:        FROM @sha256:<hash>

            var result = Regex.Replace(
                modelfile,
                @"(FROM|ADAPTER)\s+[^\s]*sha256-([a-f0-9]{64})",
                "$1 @sha256:$2"
            );

            return result;
        }

        private long ParseSize(string sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr))
            {
                return 512 * 1024 * 1024; // Default 512MB
            }

            sizeStr = sizeStr.Trim().ToUpper();

            // Extract number and unit
            long multiplier = 1;
            string numericPart = sizeStr;

            if (sizeStr.EndsWith("GB"))
            {
                multiplier = 1024 * 1024 * 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }
            else if (sizeStr.EndsWith("MB"))
            {
                multiplier = 1024 * 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }
            else if (sizeStr.EndsWith("KB"))
            {
                multiplier = 1024;
                numericPart = sizeStr.Substring(0, sizeStr.Length - 2);
            }

            if (long.TryParse(numericPart.Trim(), out long value))
            {
                return value * multiplier;
            }

            return 512 * 1024 * 1024; // Default if parsing fails
        }

        private async Task StreamBlobSourceToDestination(string sourceServer, string destServer, string sourceModel, string digest, long bufferSize)
        {
            var destClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            destClient.BaseAddress = new Uri(destServer);

            try
            {
                // Check if blob exists on destination
                var headResponse = await destClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"api/blobs/{digest}"));
                if (headResponse.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine($"  Blob already exists on destination - skipping");
                    return;
                }

                Console.WriteLine($"  Blob {digest.Substring(0, Math.Min(12, digest.Length))}... not on destination, fetching from registry");

                var registryClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
                registryClient.DefaultRequestHeaders.Add("Accept", "application/vnd.docker.distribution.manifest.v2+json, application/vnd.oci.image.manifest.v1+json");

                // Parse model name to extract namespace and model
                // Format: [namespace/]model:tag or just model:tag
                string modelNameWithoutTag = sourceModel.Split(':')[0]; // Remove tag
                string tag = sourceModel.Contains(':') ? sourceModel.Split(':')[1] : "latest";
                string registryPath;

                if (modelNameWithoutTag.Contains("/"))
                {
                    // Namespaced model (e.g., "myuser/mymodel")
                    registryPath = modelNameWithoutTag;
                }
                else
                {
                    // Library model (e.g., "llama3") - uses "library" namespace
                    registryPath = $"library/{modelNameWithoutTag}";
                }

                Console.WriteLine($"  Registry path: {registryPath}:{tag}");

                // First, try to get the manifest to understand the blob structure
                var manifestUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/manifests/{tag}",
                    $"https://registry.ollama.com/v2/{registryPath}/manifests/{tag}"
                };

                Console.WriteLine($"  Attempting to fetch manifest...");
                foreach (var manifestUrl in manifestUrls)
                {
                    try
                    {
                        var manifestResponse = await registryClient.GetAsync(manifestUrl);
                        if (manifestResponse.IsSuccessStatusCode)
                        {
                            Console.WriteLine($"  Manifest found at: {manifestUrl}");
                            var manifestContent = await manifestResponse.Content.ReadAsStringAsync();
                            Console.WriteLine($"  Manifest preview: {manifestContent.Substring(0, Math.Min(200, manifestContent.Length))}...");
                            break;
                        }
                        else
                        {
                            Console.WriteLine($"  Manifest not found at: {manifestUrl} (HTTP {manifestResponse.StatusCode})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  Manifest fetch failed: {ex.Message}");
                    }
                }

                // Now try to download the blob
                Console.WriteLine($"  Attempting to download blob...");

                // Construct registry URLs to try
                var registryUrls = new[]
                {
                    $"https://registry.ollama.ai/v2/{registryPath}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{registryPath}/blobs/{digest}",
                    // Try without library prefix
                    $"https://registry.ollama.ai/v2/{modelNameWithoutTag}/blobs/{digest}",
                    $"https://registry.ollama.com/v2/{modelNameWithoutTag}/blobs/{digest}",
                    // Fallback to direct blob access
                    $"https://registry.ollama.ai/v2/blobs/{digest}",
                    $"https://registry.ollama.com/v2/blobs/{digest}"
                };

                HttpResponseMessage downloadResponse = null;
                string successUrl = null;

                foreach (var url in registryUrls)
                {
                    try
                    {
                        Console.WriteLine($"  Trying: {url}");
                        downloadResponse = await registryClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                        Console.WriteLine($"    Response: HTTP {(int)downloadResponse.StatusCode} {downloadResponse.StatusCode}");

                        if (downloadResponse.IsSuccessStatusCode)
                        {
                            successUrl = url;
                            Console.WriteLine($"  Success! Blob found at: {url}");
                            break; // Successfully found the blob
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"    Exception: {ex.Message}");
                        continue;
                    }
                }

                if (downloadResponse == null || !downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine();
                    Console.WriteLine($"  Error: Blob not found in Ollama registry after trying all URLs");
                    Console.WriteLine($"  Last HTTP status: {downloadResponse?.StatusCode}");
                    Console.WriteLine($"  Digest: {digest}");
                    Console.WriteLine($"  Source model: {sourceModel}");
                    Console.WriteLine();
                    Console.WriteLine($"  Note: This model was likely created locally and is not available in the registry.");
                    Console.WriteLine($"  Remote-to-remote copy only works for models originally from the Ollama registry.");
                    throw new Exception("Blob not available in registry. Cannot complete remote-to-remote copy.");
                }

                long totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
                var blobSizeBytes = ByteSize.FromBytes(totalSize);

                Console.WriteLine($"transferring layer {digest}");
                Console.WriteLine($"using {ByteSize.FromBytes(bufferSize).ToString("#.##")} memory buffer");

                // Create a memory buffer stream that limits buffering and enables simultaneous download/upload
                var pipeStream = new BufferedPipeStream(bufferSize);
                var downloadStream = await downloadResponse.Content.ReadAsStreamAsync();

                // Setup progress tracking
                int block_size = 1024;
                int total_size_blocks = (int)(totalSize / block_size);
                var blob_size = ByteSize.FromKibiBytes(total_size_blocks);
                var bar = new Tqdm.ProgressBar(total: (int)blob_size.MebiBytes, useColor: GetPlatformColor(), useExpMovingAvg: false, printsPerSecond: 10);
                var stopwatch = new Stopwatch();
                stopwatch.Start();

                long bytesTransferred = 0;
                var progressLock = new object();
                int tick = 0;
                bool finished = false;

                // Start download task - reads from registry and writes to pipe
                var downloadTask = Task.Run(async () =>
                {
                    try
                    {
                        byte[] buffer = new byte[Math.Min(81920, bufferSize)]; // 80KB chunks
                        int bytesRead;
                        while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await pipeStream.WriteAsync(buffer, 0, bytesRead);
                        }
                        pipeStream.CompleteWriting();
                    }
                    catch (Exception ex)
                    {
                        pipeStream.SetException(ex);
                        throw;
                    }
                });

                // Start upload task - reads from pipe and uploads to destination
                var uploadTask = Task.Run(async () =>
                {
                    try
                    {
                        SetCursorVisible(false);

                        var uploadStream = new ProgressTrackingStream(pipeStream, totalSize, (transferred) =>
                        {
                            lock (progressLock)
                            {
                                bytesTransferred = transferred;
                                tick++;
                                if (tick < 40) { return; }
                                tick = 0;

                                double elapsedTimeInSeconds = stopwatch.Elapsed.TotalSeconds;
                                double speedInBytesPerSecond = transferred / elapsedTimeInSeconds;

                                try
                                {
                                    var sent_size = ByteSize.FromKibiBytes(transferred / block_size);
                                    int percentage = (int)sent_size.MebiBytes;
                                    bar.SetLabel($"({ByteSize.FromKibiBytes(transferred / block_size).ToString("#")} / {ByteSize.FromKibiBytes(totalSize / block_size).ToString("#")}) transferring at {ByteSize.FromKibiBytes(speedInBytesPerSecond / block_size).ToString("#")}/s");
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
                                    // Silently ignore progress bar errors
                                }
                            }
                        });

                        var uploadContent = new StreamContent(uploadStream);
                        uploadContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        uploadContent.Headers.ContentLength = totalSize;

                        var uploadResponse = await destClient.PostAsync($"api/blobs/{digest}", uploadContent);

                        finished = true;
                        SetCursorVisible(true);

                        if (!uploadResponse.IsSuccessStatusCode)
                        {
                            throw new Exception($"Upload failed with HTTP {uploadResponse.StatusCode}");
                        }

                        return uploadResponse;
                    }
                    catch (Exception ex)
                    {
                        SetCursorVisible(true);
                        throw new Exception($"Upload error: {ex.Message}", ex);
                    }
                });

                // Wait for both download and upload to complete
                await Task.WhenAll(downloadTask, uploadTask);

                var finalUploadResponse = await uploadTask;

                bar.Finish();

                if (!finalUploadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to upload to destination (HTTP {finalUploadResponse.StatusCode})");
                    System.Environment.Exit(1);
                }

                Console.WriteLine("success transferring layer");

                downloadStream.Dispose();

                registryClient.Dispose();
            }
            finally
            {
                destClient.Dispose();
            }
        }

        private async Task TransferBlobRemoteToRemote(string sourceServer, string destServer, string digest)
        {
            var originalBaseAddress = client.BaseAddress;

            try
            {
                // Check if blob already exists on destination
                client.BaseAddress = new Uri(destServer);
                var statusCode = (int)await BlobHead(digest);

                if (statusCode == 200)
                {
                    Console.WriteLine($"skipping upload for already created layer {digest}");
                    return;
                }
                else if (statusCode != 404)
                {
                    Console.WriteLine($"Error: unexpected status code {statusCode} when checking blob on destination");
                    System.Environment.Exit(1);
                }

                // Download blob from source
                Console.WriteLine($"downloading layer {digest} from source");
                client.BaseAddress = new Uri(sourceServer);

                var downloadResponse = await client.GetAsync($"api/blobs/{digest}", HttpCompletionOption.ResponseHeadersRead);

                if (!downloadResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to download blob from source (HTTP {(int)downloadResponse.StatusCode})");
                    System.Environment.Exit(1);
                }

                // Upload blob to destination
                Console.WriteLine($"uploading layer {digest} to destination");
                client.BaseAddress = new Uri(destServer);

                using (var sourceStream = await downloadResponse.Content.ReadAsStreamAsync())
                {
                    Stopwatch stopwatch = new Stopwatch();

                    try
                    {
                        SetCursorVisible(false);

                        Stream destFileStream = new ThrottledStream(sourceStream, btvalue);

                        long totalSize = downloadResponse.Content.Headers.ContentLength ?? 0;
                        int block_size = 1024;
                        var blob_size = ByteSize.FromBytes(totalSize);
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
                                var sent_size = ByteSize.FromBytes(sent);
                                percentage = (int)sent_size.MebiBytes;
                                bar.SetLabel($"({ByteSize.FromBytes(sent).ToString("#")} / {ByteSize.FromBytes(total).ToString("#")}) uploading at {ByteSize.FromBytes(speedInBytesPerSecond).ToString("#")}/s");
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
                                // Silently ignore progress bar errors
                            }
                        });

                        stopwatch.Start();
                        var uploadResponse = await client.PostAsync($"api/blobs/{digest}", streamcontent);
                        finished = true;

                        SetCursorVisible(true);

                        if (uploadResponse.IsSuccessStatusCode)
                        {
                            bar.Finish();
                            Console.WriteLine("success uploading layer");
                        }
                        else if (uploadResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
                        {
                            Console.WriteLine("Error: upload failed invalid digest, check both ollama are running the same version.");
                            System.Environment.Exit(1);
                        }
                        else
                        {
                            Console.WriteLine($"Error: upload failed: {uploadResponse.ReasonPhrase}");
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
            finally
            {
                client.BaseAddress = originalBaseAddress;
            }
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
                if (!ValidateServerUrl(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                ListRemoteModels(Destination, Pattern, sortMode).GetAwaiter().GetResult();
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

            // First try exact match with the pattern
            string regexPattern = WildcardToRegex(pattern);
            if (Regex.IsMatch(modelName, regexPattern, RegexOptions.IgnoreCase))
            {
                return true;
            }

            // If pattern doesn't contain a tag (no ':'), also try matching with :latest
            if (!pattern.Contains(":"))
            {
                string patternWithLatest = pattern + ":latest";
                string regexPatternWithLatest = WildcardToRegex(patternWithLatest);
                if (Regex.IsMatch(modelName, regexPatternWithLatest, RegexOptions.IgnoreCase))
                {
                    return true;
                }
            }

            return false;
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

        private async Task ListRemoteModels(string serverUrl, string pattern, string sortMode = "name")
        {
            try
            {
                // Set the global client's BaseAddress for the API call
                client.BaseAddress = new Uri(serverUrl);

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
                if (!ValidateServerUrl(Destination, silent: true))
                {
                    System.Environment.Exit(1);
                }
                RemoveRemoteModels(Pattern, Destination).GetAwaiter().GetResult();
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

        private async Task RemoveRemoteModels(string pattern, string destination)
        {
            // Create dedicated HttpClient with BaseAddress
            var remoteClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
            remoteClient.BaseAddress = new Uri(destination);

            try
            {
                // First get the list of models
                HttpResponseMessage response = await remoteClient.GetAsync("api/tags");

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

                        var deleteResponse = await remoteClient.SendAsync(request);

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
            finally
            {
                remoteClient.Dispose();
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

            // If pattern looks like a URL, treat it as destination and extract model name if present
            if (!string.IsNullOrEmpty(Pattern) && (Pattern.StartsWith("http://") || Pattern.StartsWith("https://")))
            {
                // Check if URL contains a model name (e.g., http://server:port/model:tag)
                var (serverUrl, modelName) = ParseRemoteSource(Pattern);

                if (!string.IsNullOrEmpty(modelName))
                {
                    // URL contains a model name, use it as the pattern
                    Destination = serverUrl;
                    Pattern = modelName;
                }
                else
                {
                    // URL is just the server, update all models
                    Destination = Pattern;
                    Pattern = "*";
                }
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
                if (!ValidateServerUrl(Destination))
                {
                    System.Environment.Exit(1);
                }
                UpdateRemoteModels(Pattern, Destination).GetAwaiter().GetResult();
            }
        }

        public void ActionPull(string modelName, string destination)
        {
            Init();

            bool isHuggingFaceModel = false;

            // Convert HuggingFace URL to ollama format if needed
            if (IsHuggingFaceUrl(modelName))
            {
                modelName = ConvertHuggingFaceUrl(modelName);
                isHuggingFaceModel = true;
            }
            else if (modelName.StartsWith("hf.co/", StringComparison.OrdinalIgnoreCase))
            {
                isHuggingFaceModel = true;
            }

            // Add :latest tag ONLY if:
            // 1. No tag is specified (doesn't contain ':')
            // 2. NOT a HuggingFace model (HF models always have explicit tags)
            if (!modelName.Contains(":") && !isHuggingFaceModel)
            {
                modelName = $"{modelName}:latest";
            }

            Debug.WriteLine($"Pull: pulling model '{modelName}' to '{destination ?? "local"}'");

            // Route to local or remote pull
            if (string.IsNullOrEmpty(destination))
            {
                PullLocalModel(modelName);
            }
            else
            {
                if (!ValidateServerUrl(destination))
                {
                    System.Environment.Exit(1);
                }
                PullRemoteModel(modelName, destination).GetAwaiter().GetResult();
            }
        }

        private bool IsHuggingFaceUrl(string input)
        {
            return input.StartsWith("https://huggingface.co/", StringComparison.OrdinalIgnoreCase) ||
                   input.StartsWith("http://huggingface.co/", StringComparison.OrdinalIgnoreCase);
        }

        private string ConvertHuggingFaceUrl(string huggingFaceUrl)
        {
            try
            {
                // Parse URL: https://huggingface.co/{namespace}/{repo}/blob/{branch}/{filename}.gguf
                Uri uri = new Uri(huggingFaceUrl);
                string[] pathParts = uri.AbsolutePath.TrimStart('/').Split('/');

                if (pathParts.Length < 5)
                {
                    Console.WriteLine($"Error: Invalid HuggingFace URL format: {huggingFaceUrl}");
                    Console.WriteLine($"Expected format: https://huggingface.co/{{namespace}}/{{repo}}/blob/{{branch}}/{{filename}}.gguf");
                    System.Environment.Exit(1);
                }

                string namespacePart = pathParts[0];
                string repo = pathParts[1];
                string filename = pathParts[4];

                // Remove .gguf extension
                if (filename.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
                {
                    filename = filename.Substring(0, filename.Length - 5);
                }

                // Extract tag (quantization) from filename
                string tag = ExtractTagFromFilename(filename, repo);

                // Construct ollama-compatible format
                string ollamaFormat = $"hf.co/{namespacePart}/{repo}:{tag}";

                Console.WriteLine($"Converted HuggingFace URL to: {ollamaFormat}");
                return ollamaFormat;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: Failed to convert HuggingFace URL: {ex.Message}");
                System.Environment.Exit(1);
                return null;
            }
        }

        private string ExtractTagFromFilename(string filename, string repo)
        {
            // Common GGUF quantization patterns
            var quantizationPatterns = new[]
            {
                @"IQ\d+_[A-Z]+",          // IQ2_M, IQ3_XXS, IQ4_XS, etc.
                @"Q\d+_K_[A-Z]+",         // Q4_K_M, Q5_K_S, Q6_K, etc.
                @"Q\d+_K",                // Q4_K, Q5_K, etc.
                @"Q\d+_\d+",              // Q4_0, Q5_1, Q8_0, etc.
                @"F\d+",                  // F16, F32
                @"FP\d+",                 // FP16, FP32
                @"BF\d+",                 // BF16 (bfloat16)
                @"[A-Z]+\d+[A-Z]*"       // Catch-all for other formats
            };

            // Try each pattern to find quantization identifier
            foreach (var pattern in quantizationPatterns)
            {
                var match = Regex.Match(filename, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return match.Value.ToUpper();
                }
            }

            // Fallback: Try to extract the last segment after '-'
            var parts = filename.Split('-');
            if (parts.Length > 1)
            {
                var lastPart = parts[parts.Length - 1];

                // Check if last part looks like a quantization
                if (Regex.IsMatch(lastPart, @"[A-Z]+\d+|[A-Z]+_[A-Z]+", RegexOptions.IgnoreCase))
                {
                    return lastPart.ToUpper();
                }
            }

            // Ultimate fallback: Use the full filename
            Console.WriteLine($"Warning: Could not detect quantization format from filename '{filename}', using full filename as tag");
            return filename;
        }

        private void PullLocalModel(string modelName)
        {
            Console.WriteLine($"Pulling '{modelName}' locally...\n");

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = $"pull {modelName}";
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = false;
            // Don't redirect output - let it go directly to console for proper ANSI handling
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.RedirectStandardError = false;

            p.Start();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"\nError: Failed to pull model '{modelName}'");
                System.Environment.Exit(1);
            }

            Console.WriteLine($"\n✓ Successfully pulled '{modelName}'");
        }

        private async Task PullRemoteModel(string modelName, string destination)
        {
            Console.WriteLine($"Pulling '{modelName}' to remote server {destination}...\n");

            var httpClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            httpClient.BaseAddress = new Uri(destination);

            try
            {
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

                var response = await httpClient.PostAsync("api/pull", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to pull model (HTTP {response.StatusCode})");
                    System.Environment.Exit(1);
                }

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string lastDigest = null;
                string lastStatus = null;
                string lastDisplayedProgress = null;
                var completedDigests = new HashSet<string>();

                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        using var lineDoc = JsonDocument.Parse(line);
                        var root = lineDoc.RootElement;

                        if (root.TryGetProperty("error", out var errorProp))
                        {
                            Console.WriteLine($"\nError: {errorProp.GetString()}");
                            System.Environment.Exit(1);
                        }

                        if (root.TryGetProperty("status", out var statusProp))
                        {
                            string status = statusProp.GetString();
                            string digest = root.TryGetProperty("digest", out var digestProp) ? digestProp.GetString() : null;
                            long total = root.TryGetProperty("total", out var totalProp) ? totalProp.GetInt64() : 0;
                            long completed = root.TryGetProperty("completed", out var completedProp) ? completedProp.GetInt64() : 0;

                            // Check if this is a new digest or different status
                            bool isNewDigest = digest != null && digest != lastDigest;
                            bool isNewStatus = status != lastStatus || isNewDigest;

                            // Handle new status or digest
                            if (isNewStatus)
                            {
                                // Print newline if we were showing progress
                                if (lastDisplayedProgress != null)
                                {
                                    Console.WriteLine();
                                    lastDisplayedProgress = null;
                                }

                                // Print status for non-pulling messages
                                if (!status.StartsWith("pulling ") || digest == null)
                                {
                                    Console.WriteLine(status);
                                }
                                else if (isNewDigest)
                                {
                                    // New digest being pulled
                                    string digestShort = digest.Length > 12 ? digest.Substring(0, 12) : digest;
                                    Console.Write($"pulling {digestShort}");
                                }

                                lastStatus = status;
                                lastDigest = digest;
                            }

                            // Show progress for pulling operations (but skip if already completed)
                            if (digest != null && status.StartsWith("pulling ") && total > 0 && !completedDigests.Contains(digest))
                            {
                                var completedSize = ByteSize.FromBytes(completed);
                                var totalSize = ByteSize.FromBytes(total);
                                int percentage = total > 0 ? (int)((completed * 100) / total) : 0;

                                string progressDisplay = $" {percentage}% ({completedSize.ToString("#.##")} / {totalSize.ToString("#.##")})";

                                // Only update if progress changed significantly (avoid too many updates)
                                if (progressDisplay != lastDisplayedProgress)
                                {
                                    Console.Write($"\r{status.Substring(0, Math.Min(status.Length, 40))}{progressDisplay}");
                                    lastDisplayedProgress = progressDisplay;
                                }

                                // If completed, print newline and mark as completed
                                if (completed >= total)
                                {
                                    Console.WriteLine($" 100%");
                                    lastDisplayedProgress = null;
                                    completedDigests.Add(digest);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Skip malformed JSON lines
                    }
                }

                // Clean up any remaining progress line
                if (lastDisplayedProgress != null)
                {
                    Console.WriteLine();
                }

                Console.WriteLine($"\n✓ Successfully pulled '{modelName}' to remote server");
            }
            finally
            {
                httpClient.Dispose();
            }
        }

        public void ActionShow(string modelName, string destination, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            Init();

            // Add :latest tag if not specified
            if (!modelName.Contains(":"))
            {
                modelName = $"{modelName}:latest";
            }

            Debug.WriteLine($"Show: showing model '{modelName}' at '{destination ?? "local"}'");

            // Route to local or remote show
            if (string.IsNullOrEmpty(destination))
            {
                ShowLocalModel(modelName, license, modelfile, parameters, system, template, verbose);
            }
            else
            {
                if (!ValidateServerUrl(destination))
                {
                    System.Environment.Exit(1);
                }
                ShowRemoteModel(modelName, destination, license, modelfile, parameters, system, template, verbose).GetAwaiter().GetResult();
            }
        }

        private void ShowLocalModel(string modelName, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            // Build arguments for ollama show command
            var args = new List<string> { "show", modelName };

            if (license) args.Add("--license");
            if (modelfile) args.Add("--modelfile");
            if (parameters) args.Add("--parameters");
            if (system) args.Add("--system");
            if (template) args.Add("--template");
            if (verbose) args.Add("--verbose");

            var p = new Process();
            p.StartInfo.FileName = "ollama";
            p.StartInfo.Arguments = string.Join(" ", args);
            p.StartInfo.CreateNoWindow = false;
            p.StartInfo.UseShellExecute = false;
            // Don't redirect output - let it go directly to console for proper formatting
            p.StartInfo.RedirectStandardOutput = false;
            p.StartInfo.RedirectStandardError = false;

            p.Start();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"Error: Failed to show model '{modelName}'");
                System.Environment.Exit(1);
            }
        }

        private async Task ShowRemoteModel(string modelName, string destination, bool license, bool modelfile, bool parameters, bool system, bool template, bool verbose)
        {
            // Create dedicated HttpClient with BaseAddress
            var httpClient = new HttpClient() { Timeout = TimeSpan.FromMinutes(5) };
            httpClient.BaseAddress = new Uri(destination);

            try
            {
                var showRequest = new
                {
                    name = modelName,
                    verbose = verbose
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(showRequest),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await httpClient.PostAsync("api/show", content);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: Failed to show model (HTTP {response.StatusCode})");
                    System.Environment.Exit(1);
                }

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // If specific flags are set, show only those parts
                bool hasSpecificFlag = license || modelfile || parameters || system || template;

                if (license && root.TryGetProperty("license", out var licenseElement))
                {
                    Console.WriteLine(licenseElement.GetString());
                }
                else if (modelfile && root.TryGetProperty("modelfile", out var modelfileElement))
                {
                    Console.WriteLine(modelfileElement.GetString());
                }
                else if (parameters && root.TryGetProperty("parameters", out var parametersElement))
                {
                    Console.WriteLine(parametersElement.GetString());
                }
                else if (system && root.TryGetProperty("system", out var systemElement))
                {
                    Console.WriteLine(systemElement.GetString());
                }
                else if (template && root.TryGetProperty("template", out var templateElement))
                {
                    Console.WriteLine(templateElement.GetString());
                }
                else if (!hasSpecificFlag)
                {
                    // Show default information (similar to ollama show without flags)
                    if (root.TryGetProperty("modelfile", out var mf))
                    {
                        Console.WriteLine(mf.GetString());
                    }

                    if (verbose)
                    {
                        // Show detailed information
                        Console.WriteLine();
                        if (root.TryGetProperty("license", out var lic))
                        {
                            Console.WriteLine("License:");
                            Console.WriteLine(lic.GetString());
                            Console.WriteLine();
                        }
                        if (root.TryGetProperty("parameters", out var param))
                        {
                            Console.WriteLine("Parameters:");
                            Console.WriteLine(param.GetString());
                            Console.WriteLine();
                        }
                        if (root.TryGetProperty("system", out var sys))
                        {
                            Console.WriteLine("System:");
                            Console.WriteLine(sys.GetString());
                            Console.WriteLine();
                        }
                        if (root.TryGetProperty("template", out var tmpl))
                        {
                            Console.WriteLine("Template:");
                            Console.WriteLine(tmpl.GetString());
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: Failed to show model: {e.Message}");
                System.Environment.Exit(1);
            }
            finally
            {
                httpClient.Dispose();
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
            // Create dedicated client for remote server
            var remoteClient = new HttpClient() { Timeout = TimeSpan.FromHours(1) };
            remoteClient.BaseAddress = new Uri(destination);

            try
            {
                // First get the list of models
                HttpResponseMessage response = await remoteClient.GetAsync("api/tags");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error: failed to get models from remote server (HTTP {(int)response.StatusCode})");
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
                    return;
                }

                Console.WriteLine($"Updating {modelsToUpdate.Count} model(s) on remote server...\n");

                foreach (var modelName in modelsToUpdate)
                {
                    await UpdateSingleRemoteModel(remoteClient, modelName, destination);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                System.Environment.Exit(1);
            }
            finally
            {
                remoteClient.Dispose();
            }
        }

        private async Task UpdateSingleRemoteModel(HttpClient httpClient, string modelName, string destination)
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

                var response = await httpClient.PostAsync("api/pull", content);

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

        public void ActionInstall()
        {
            Init();

            Console.WriteLine("Installing osync...\n");
            Console.WriteLine($"Installing osync... {System.Environment.ProcessPath}\n"); 

            // Detect operating system
            bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            bool isMacOS = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX);

            // Get current application directory
            string currentExeDir = AppContext.BaseDirectory;
            string exeName = isWindows ? "osync.exe" : "osync";
            string currentExePath = Path.Combine(currentExeDir, exeName);

            // Determine installation directory
            string installDir;
            if (isWindows)
            {
                installDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".osync");
            }
            else // Linux or macOS
            {
                installDir = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local", "bin");
            }

            // Create installation directory if it doesn't exist
            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
                Console.WriteLine($"Created directory: {installDir}");
            }

            try
            {
                // Check if we're already running from the install directory
                if (Path.GetFullPath(currentExeDir).Equals(Path.GetFullPath(installDir), StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"✓ osync is already installed in: {installDir}");
                }
                else
                {
                    // Copy all files from the application directory
                    // This includes the exe, dlls, and all dependencies
                    int filesCopied = 0;

                    foreach (string sourceFile in Directory.GetFiles(currentExeDir, "*", SearchOption.TopDirectoryOnly))
                    {
                        string fileName = Path.GetFileName(sourceFile);

                        // Skip unnecessary files
                        if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                            fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string targetFile = Path.Combine(installDir, fileName);
                        System.IO.File.Copy(sourceFile, targetFile, true);
                        filesCopied++;
                    }

                    Console.WriteLine($"✓ Copied osync and {filesCopied - 1} dependency files to: {installDir}");

                    // On Unix systems, make the executable file executable
                    if (isLinux || isMacOS)
                    {
                        try
                        {
                            var chmodProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "chmod",
                                    Arguments = $"+x \"{Path.Combine(installDir, exeName)}\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                }
                            };
                            chmodProcess.Start();
                            chmodProcess.WaitForExit();
                        }
                        catch
                        {
                            // chmod might fail, but that's okay
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying files: {ex.Message}");
                System.Environment.Exit(1);
            }

            // Add to PATH if not already there
            bool pathUpdated = AddToPath(installDir, isWindows, isLinux, isMacOS);

            if (pathUpdated)
            {
                Console.WriteLine($"✓ Added {installDir} to PATH");
            }
            else
            {
                Console.WriteLine($"✓ {installDir} is already in PATH");
            }

            // Check if shell completion is available before asking
            Console.WriteLine();
            bool completionAvailable = true;
            string completionWarning = "";

            if (isWindows)
            {
                // Check PowerShell version first
                try
                {
                    string psExecutable = DetectPowerShellExecutable();
                    var (version, edition) = GetPowerShellVersion(psExecutable);

                    if (version.Major < 6)
                    {
                        completionAvailable = false;
                        completionWarning = $"Shell completion is not available (PowerShell {version} detected, requires 6.0+)";
                    }
                }
                catch (Exception ex)
                {
                    completionAvailable = false;
                    completionWarning = $"Could not detect PowerShell version: {ex.Message}";
                }
            }

            if (completionAvailable)
            {
                Console.Write("Would you like to install shell completion? (y/n): ");
                string response = Console.ReadLine()?.Trim().ToLower();

                if (response == "y" || response == "yes")
                {
                    Console.WriteLine();
                    if (isWindows)
                    {
                        InstallPowerShellCompletion();
                    }
                    else if (isLinux || isMacOS)
                    {
                        InstallBashCompletion();
                    }
                }
                else
                {
                    Console.WriteLine("Skipping shell completion installation.");
                }
            }
            else
            {
                Console.WriteLine(completionWarning);
                Console.WriteLine("\nTo install shell completion later:");
                if (isWindows)
                {
                    Console.WriteLine("  1. Install PowerShell 7+: https://github.com/PowerShell/PowerShell/releases");
                    Console.WriteLine("  2. Run: osync install");
                }
            }

            // Final instructions
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("Installation complete!");
            Console.WriteLine(new string('=', 60));

            if (isWindows)
            {
                Console.WriteLine("\nNext steps:");
                Console.WriteLine("  1. Restart your terminal to reload PATH");
                Console.WriteLine("  2. Verify installation: osync --help");
            }
            else
            {
                Console.WriteLine("\nNext steps:");
                Console.WriteLine("  1. Reload your shell configuration:");
                if (isMacOS)
                {
                    Console.WriteLine("     source ~/.zshrc  (or ~/.bash_profile)");
                }
                else
                {
                    Console.WriteLine("     source ~/.bashrc");
                }
                Console.WriteLine("  2. Verify installation: osync --help");
            }
        }

        private bool AddToPath(string directory, bool isWindows, bool isLinux, bool isMacOS)
        {
            if (isWindows)
            {
                return AddToPathWindows(directory);
            }
            else if (isLinux || isMacOS)
            {
                return AddToPathUnix(directory, isMacOS);
            }
            return false;
        }

        private bool AddToPathWindows(string directory)
        {
            try
            {
                // Get user PATH environment variable
                string userPath = System.Environment.GetEnvironmentVariable("PATH", System.EnvironmentVariableTarget.User) ?? "";

                // Normalize the directory path
                string normalizedDirectory = Path.GetFullPath(directory);

                // Check if directory is already in PATH
                string[] paths = userPath.Split(';');
                foreach (string path in paths)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        continue;

                    try
                    {
                        string normalizedPath = Path.GetFullPath(path.Trim());
                        if (normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                        {
                            return false; // Already in PATH
                        }
                    }
                    catch
                    {
                        // Skip invalid paths
                        continue;
                    }
                }

                // Add to PATH
                string newPath = string.IsNullOrEmpty(userPath) ? directory : userPath.TrimEnd(';') + ";" + directory;
                System.Environment.SetEnvironmentVariable("PATH", newPath, System.EnvironmentVariableTarget.User);

                // Broadcast environment variable change to the system
                BroadcastEnvironmentChange();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not update PATH: {ex.Message}");
                Console.WriteLine($"Please add {directory} to your PATH manually.");
                return false;
            }
        }

        private void BroadcastEnvironmentChange()
        {
            try
            {
                // Notify the system that environment variables have changed
                // This allows currently running applications to refresh their environment
                const int HWND_BROADCAST = 0xffff;
                const uint WM_SETTINGCHANGE = 0x001a;
                IntPtr result;
                SendMessageTimeout(
                    new IntPtr(HWND_BROADCAST),
                    WM_SETTINGCHANGE,
                    IntPtr.Zero,
                    "Environment",
                    0,
                    1000,
                    out result);
            }
            catch
            {
                // If broadcast fails, it's not critical - PATH is still updated
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint Msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out IntPtr lpdwResult);

        private bool AddToPathUnix(string directory, bool isMacOS)
        {
            try
            {
                string homeDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
                string[] rcFiles;

                if (isMacOS)
                {
                    // macOS uses zsh by default since Catalina
                    rcFiles = new[] {
                        Path.Combine(homeDir, ".zshrc"),
                        Path.Combine(homeDir, ".bash_profile")
                    };
                }
                else
                {
                    // Linux typically uses bash
                    rcFiles = new[] {
                        Path.Combine(homeDir, ".bashrc"),
                        Path.Combine(homeDir, ".bash_profile")
                    };
                }

                string pathLine = $"\nexport PATH=\"{directory}:$PATH\"\n";
                bool addedToAny = false;

                foreach (string rcFile in rcFiles)
                {
                    // For macOS, try .zshrc first, if it exists use only that
                    // For Linux, try .bashrc first, if it exists use only that
                    if (System.IO.File.Exists(rcFile))
                    {
                        string content = System.IO.File.ReadAllText(rcFile);

                        // Check if already in file
                        if (content.Contains($"export PATH=\"{directory}:$PATH\"") ||
                            content.Contains($"export PATH='{directory}:$PATH'") ||
                            content.Contains($"PATH=\"{directory}:$PATH\"") ||
                            content.Contains($"PATH='{directory}:$PATH'"))
                        {
                            return false; // Already configured
                        }

                        // Add PATH export
                        System.IO.File.AppendAllText(rcFile, pathLine);
                        Console.WriteLine($"Added PATH export to: {rcFile}");
                        return true;
                    }
                }

                // If no rc file exists, create .bashrc (Linux) or .zshrc (macOS)
                string defaultRc = isMacOS ? rcFiles[0] : rcFiles[0];
                System.IO.File.WriteAllText(defaultRc, pathLine);
                Console.WriteLine($"Created {defaultRc} with PATH export");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not update shell configuration: {ex.Message}");
                Console.WriteLine($"Please add {directory} to your PATH manually by adding this line to your shell rc file:");
                Console.WriteLine($"  export PATH=\"{directory}:$PATH\"");
                return false;
            }
        }

        private void InstallBashCompletion()
        {
            try
            {
                // Get the completion script content
                string completionScript = GetBashCompletionScript();

                // Convert Windows line endings (CRLF) to Unix line endings (LF)
                completionScript = completionScript.Replace("\r\n", "\n");

                // Determine installation path
                string installPath = null;
                string[] possiblePaths = new[]
                {
                    "/etc/bash_completion.d/osync",
                    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".local/share/bash-completion/completions/osync"),
                    Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".bash_completion.d/osync")
                };

                // Try to write to system location first, then user locations
                foreach (var path in possiblePaths)
                {
                    try
                    {
                        string directory = Path.GetDirectoryName(path);
                        if (!Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        System.IO.File.WriteAllText(path, completionScript);
                        installPath = path;
                        Console.WriteLine($"✓ Bash completion installed to: {path}");
                        break;
                    }
                    catch
                    {
                        // Try next location
                        continue;
                    }
                }

                if (installPath == null)
                {
                    // Fallback: Add to .bashrc
                    string bashrc = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".bashrc");
                    // Note: completionScript already has Unix line endings from conversion above
                    string sourceCommand = "\n# osync completion\n" + completionScript + "\n";

                    // Check if already exists
                    if (System.IO.File.Exists(bashrc))
                    {
                        string bashrcContent = System.IO.File.ReadAllText(bashrc);
                        if (!bashrcContent.Contains("# osync completion"))
                        {
                            System.IO.File.AppendAllText(bashrc, sourceCommand);
                            Console.WriteLine($"✓ Bash completion added to: {bashrc}");
                        }
                        else
                        {
                            Console.WriteLine($"✓ Bash completion already configured in: {bashrc}");
                        }
                    }
                    else
                    {
                        System.IO.File.WriteAllText(bashrc, sourceCommand);
                        Console.WriteLine($"✓ Bash completion added to: {bashrc}");
                    }
                }

                Console.WriteLine("\nTo activate completion, run:");
                Console.WriteLine("  source ~/.bashrc");
                Console.WriteLine("Or restart your terminal.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing bash completion: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        private void InstallPowerShellCompletion()
        {
            try
            {
                // Detect which PowerShell executable to use (pwsh or powershell)
                string psExecutable = DetectPowerShellExecutable();

                // Check PowerShell version
                var (version, edition) = GetPowerShellVersion(psExecutable);

                Console.WriteLine($"Detected: PowerShell {version} ({edition})");

                // Check if version is >= 6.0
                if (version.Major < 6)
                {
                    Console.WriteLine("\nError: PowerShell version 6.0 or higher is required for completion support.");
                    Console.WriteLine($"Current version: {version} ({edition})");
                    Console.WriteLine("\nPowerShell Desktop 5.x does not support the ArgumentCompleter used by osync.");
                    Console.WriteLine("\nPlease install PowerShell 7+ from:");
                    Console.WriteLine("  https://github.com/PowerShell/PowerShell/releases");
                    Console.WriteLine("\nOr use the interactive mode completion which works on all PowerShell versions:");
                    Console.WriteLine("  osync  (then press TAB)");
                    System.Environment.Exit(1);
                }

                // Get PowerShell profile path from the detected PowerShell
                string profilePath = GetPowerShellProfilePath(psExecutable);

                if (string.IsNullOrEmpty(profilePath))
                {
                    Console.WriteLine("Error: Could not determine PowerShell profile path");
                    System.Environment.Exit(1);
                }

                Console.WriteLine($"PowerShell profile: {profilePath}");

                // Create profile directory if it doesn't exist
                string profileDir = Path.GetDirectoryName(profilePath);
                if (!Directory.Exists(profileDir))
                {
                    Directory.CreateDirectory(profileDir);
                    Console.WriteLine($"Created profile directory: {profileDir}");
                }

                // Get completion script content
                string completionScript = GetPowerShellCompletionScript();

                // Check if profile exists
                bool profileExists = System.IO.File.Exists(profilePath);
                string profileContent = profileExists ? System.IO.File.ReadAllText(profilePath) : "";

                // Check if osync completion is already configured
                if (profileContent.Contains("# osync PowerShell completion - START"))
                {
                    Console.WriteLine("Removing existing osync completion configuration...");

                    // Remove old completion block using START and END markers
                    int startIndex = profileContent.IndexOf("# osync PowerShell completion - START");
                    if (startIndex >= 0)
                    {
                        // Find the end marker
                        int endIndex = profileContent.IndexOf("# osync PowerShell completion - END", startIndex);
                        if (endIndex >= 0)
                        {
                            // Include the END marker line in removal
                            endIndex = profileContent.IndexOf("\n", endIndex);
                            if (endIndex < 0)
                            {
                                endIndex = profileContent.Length;
                            }
                            else
                            {
                                endIndex++; // Include the newline
                            }

                            // Remove the completion block
                            profileContent = profileContent.Remove(startIndex, endIndex - startIndex);
                        }
                        else
                        {
                            // Old format without END marker - remove until next section or EOF
                            int legacyEndIndex = profileContent.IndexOf("\n# ", startIndex + 1);
                            if (legacyEndIndex < 0)
                            {
                                legacyEndIndex = profileContent.IndexOf("\nfunction ", startIndex + 1);
                            }
                            if (legacyEndIndex < 0)
                            {
                                legacyEndIndex = profileContent.Length;
                            }
                            profileContent = profileContent.Remove(startIndex, legacyEndIndex - startIndex);
                        }

                        // Remove trailing newlines
                        profileContent = profileContent.TrimEnd('\r', '\n');
                    }
                }

                // Add completion script to profile
                string completionBlock = "\n\n" + completionScript;
                profileContent += completionBlock;

                // Write updated profile
                System.IO.File.WriteAllText(profilePath, profileContent);

                if (profileExists)
                {
                    Console.WriteLine($"✓ Updated osync completion in profile: {profilePath}");
                }
                else
                {
                    Console.WriteLine($"✓ Created profile and added osync completion: {profilePath}");
                }

                Console.WriteLine("\nTo activate completion:");
                Console.WriteLine("  1. Restart PowerShell, or");
                Console.WriteLine($"  2. Run: . {profilePath}");
                Console.WriteLine("\nNote: You may need to set execution policy:");
                Console.WriteLine("  Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing PowerShell completion: {ex.Message}");
                System.Environment.Exit(1);
            }
        }

        private string DetectPowerShellExecutable()
        {
            // Detect which PowerShell is currently running by checking parent process
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var parentProcess = GetParentProcess(currentProcess);

                if (parentProcess != null)
                {
                    string parentName = parentProcess.ProcessName.ToLower();

                    Debug.WriteLine($"Parent process: {parentName} (PID: {parentProcess.Id})");

                    // Check if parent is pwsh (PowerShell Core/7+)
                    if (parentName == "pwsh")
                    {
                        Debug.WriteLine("Detected parent as pwsh (PowerShell Core)");
                        return parentProcess.MainModule.FileName; // Return full path to the actual executable
                    }
                    // Check if parent is powershell (Windows PowerShell 5.x)
                    else if (parentName == "powershell")
                    {
                        Debug.WriteLine("Detected parent as powershell (Windows PowerShell)");
                        return parentProcess.MainModule.FileName; // Return full path to the actual executable
                    }
                    else
                    {
                        Debug.WriteLine($"Parent is not PowerShell, it's: {parentName}");
                    }
                }
                else
                {
                    Debug.WriteLine("Failed to get parent process");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception in parent process detection: {ex.Message}");
                // If parent process detection fails, fall through to availability check
            }

            // Fallback: Check which PowerShell is available
            // Try pwsh first (PowerShell Core/7+)
            Debug.WriteLine("Falling back to availability check");
            try
            {
                var p = new Process();
                p.StartInfo.FileName = "pwsh";
                p.StartInfo.Arguments = "-NoProfile -Command \"exit\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.Start();
                p.WaitForExit();

                if (p.ExitCode == 0)
                {
                    return "pwsh";
                }
            }
            catch
            {
                // pwsh not found, try powershell
            }

            // Final fallback to powershell (Windows PowerShell 5.x)
            return "powershell";
        }

        private Process GetParentProcess(Process process)
        {
            try
            {
                var parentPid = 0;
                var handle = process.Handle;

                // Use WMI to get parent process ID on Windows
                var query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {process.Id}";
                using (var searcher = new System.Management.ManagementObjectSearcher(query))
                {
                    foreach (System.Management.ManagementObject obj in searcher.Get())
                    {
                        parentPid = Convert.ToInt32(obj["ParentProcessId"]);
                        break;
                    }
                }

                if (parentPid > 0)
                {
                    return Process.GetProcessById(parentPid);
                }
            }
            catch
            {
                // Failed to get parent process
            }

            return null;
        }

        private (Version version, string edition) GetPowerShellVersion(string psExecutable)
        {
            try
            {
                Debug.WriteLine($"Getting PowerShell version for: {psExecutable}");

                var p = new Process();
                p.StartInfo.FileName = psExecutable;
                p.StartInfo.Arguments = "-NoProfile -Command \"$PSVersionTable.PSVersion.ToString(); $PSVersionTable.PSEdition\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                Debug.WriteLine($"PowerShell version output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"PowerShell version error: {error}");
                }

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var lines = output.Trim().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length >= 2)
                    {
                        Version version = Version.Parse(lines[0].Trim());
                        string edition = lines[1].Trim();
                        Debug.WriteLine($"Parsed version: {version}, edition: {edition}");
                        return (version, edition);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception getting PowerShell version: {ex.Message}");
                // Return default if detection fails
            }

            Debug.WriteLine("Failed to get version, returning default (5.1, Desktop)");
            return (new Version(5, 1), "Desktop");
        }

        private string GetPowerShellProfilePath(string psExecutable)
        {
            // Get profile path from the specified PowerShell executable using $PROFILE
            try
            {
                Debug.WriteLine($"Getting profile path for: {psExecutable}");

                var p = new Process();
                p.StartInfo.FileName = psExecutable;
                p.StartInfo.Arguments = "-NoProfile -Command \"$PROFILE\"";
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;

                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit();

                Debug.WriteLine($"Profile path output: {output}");
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.WriteLine($"Profile path error: {error}");
                }

                if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    string profilePath = output.Trim();
                    Debug.WriteLine($"Using profile path: {profilePath}");
                    return profilePath;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Exception getting profile path: {ex.Message}");
                // Fallback to default path
            }

            // Fallback to default PowerShell profile location based on executable
            string documentsPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
            string executableLower = psExecutable.ToLower();

            if (executableLower.Contains("pwsh"))
            {
                // PowerShell Core/7+ uses "PowerShell" folder
                Debug.WriteLine("Falling back to pwsh profile path");
                return Path.Combine(documentsPath, "PowerShell", "Microsoft.PowerShell_profile.ps1");
            }
            else
            {
                // Windows PowerShell 5.x uses "WindowsPowerShell" folder
                Debug.WriteLine("Falling back to powershell profile path");
                return Path.Combine(documentsPath, "WindowsPowerShell", "Microsoft.PowerShell_profile.ps1");
            }
        }

        private string GetBashCompletionScript()
        {
            return @"#!/usr/bin/env bash
# osync bash completion script

_osync_completions() {
    local cur prev opts commands
    COMPREPLY=()
    cur=""${COMP_WORDS[COMP_CWORD]}""
    prev=""${COMP_WORDS[COMP_CWORD-1]}""

    # Available commands
    commands=""cp copy ls list rm remove del delete update show run chat ps load unload""

    # Global options
    opts=""-bt -BufferSize --size --sizeasc --time --timeasc --license --modelfile --parameters --system --template -v --verbose -h -? -d --format --keepalive --dimensions --think --hide-thinking --insecure --truncate --no-wordwrap""

    # Get list of models using osync (supports both local and remote)
    _get_models() {
        local server_url=""$1""

        # Build command and filter output
        # Skip version line, blank lines, and header (NAME/Model)
        if [[ -n ""$server_url"" ]]; then
            osync ls -d ""$server_url"" 2>/dev/null | \
                grep -v ""^osync"" | \
                grep -v ""^NAME"" | \
                grep -v ""^Model"" | \
                grep -v ""^$"" | \
                awk '{print $1}' 2>/dev/null
        else
            osync ls 2>/dev/null | \
                grep -v ""^osync"" | \
                grep -v ""^NAME"" | \
                grep -v ""^Model"" | \
                grep -v ""^$"" | \
                awk '{print $1}' 2>/dev/null
        fi
    }

    # First argument - complete commands
    if [[ ${COMP_CWORD} -eq 1 ]]; then
        COMPREPLY=( $(compgen -W ""${commands}"" -- ""${cur}"") )
        return 0
    fi

    # Get the command (first argument)
    local command=""${COMP_WORDS[1]}""

    # Extract remote server URL from the raw command line
    # COMP_WORDS splits on colons, so we use COMP_LINE instead
    local server_url=""""

    # Look for -d flag followed by URL in the raw command line
    if [[ ""$COMP_LINE"" =~ -d[[:space:]]+(https?://[^[:space:]]+) ]]; then
        server_url=""${BASH_REMATCH[1]}""
    fi

    # Get models for completion (from local or remote server)
    local models=$(_get_models ""$server_url"")

    # Complete based on command
    case ""${command}"" in
        cp|copy)
            # Complete model names or options
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-bt -BufferSize -d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        ls|list)
            # Complete model name patterns or options
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""--size --sizeasc --time --timeasc -d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        rm|remove|del|delete)
            # Complete model names or options
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        update)
            # Complete model names or options
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        show)
            # Complete model names and flags
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""--license --modelfile --parameters --system --template -v --verbose -d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        run|chat)
            # Complete model names and flags
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d --verbose --no-wordwrap --format --keepalive --dimensions --think --hide-thinking --insecure --truncate"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        ps)
            # Complete flags only (no model names for ps)
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            fi
            ;;
        load)
            # Complete model names and flags
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        unload)
            # Complete model names and flags
            if [[ ${cur} == -* ]]; then
                COMPREPLY=( $(compgen -W ""-d"" -- ""${cur}"") )
            else
                COMPREPLY=( $(compgen -W ""${models}"" -- ""${cur}"") )
            fi
            ;;
        *)
            ;;
    esac
}

complete -F _osync_completions osync";
        }

        private string GetPowerShellCompletionScript()
        {
            return @"# osync PowerShell completion - START
# This script is automatically installed by 'osync install'

# Function to get list of models using osync (supports both local and remote)
function Get-OsyncModels {
    param(
        [string]$ServerUrl = """"
    )

    try {
        # Build command arguments
        $args = @(""ls"")
        if ($ServerUrl) {
            $args += @(""-d"", $ServerUrl)
        }

        # Execute osync ls command
        $output = & osync $args 2>$null

        if ($output -and $output.Count -gt 0) {
            # Skip first 3 lines (version line, blank line, header line)
            $models = $output | Select-Object -Skip 3 | ForEach-Object {
                # Extract model name from the line (first column)
                if ($_ -match '^\s*(\S+)') {
                    $matches[1]
                }
            } | Where-Object { $_ }
            return $models
        }
    }
    catch {
        return @()
    }
    return @()
}

# Register argument completer for osync (native executable)
Register-ArgumentCompleter -Native -CommandName osync -ScriptBlock {
    param($wordToComplete, $commandAst, $cursorPosition)

    $commands = @('cp', 'copy', 'ls', 'list', 'rm', 'remove', 'del', 'delete', 'update', 'show', 'run', 'chat', 'ps', 'load', 'unload')

    # Get all words in the command line
    $words = $commandAst.ToString() -split '\s+'

    # If we're completing the first argument (command)
    if ($words.Count -eq 1 -or ($words.Count -eq 2 -and $wordToComplete)) {
        $commands | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
            [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
        }
        return
    }

    # Get the command (first argument after osync)
    $command = $words[1]

    # Extract remote server URL if present (look for http:// or https://)
    $serverUrl = """"
    foreach ($word in $words) {
        if ($word -match '^https?://') {
            $serverUrl = $word
            break
        }
    }

    # Get models for completion (from local or remote server)
    $models = Get-OsyncModels -ServerUrl $serverUrl

    # Complete based on command
    switch ($command) {
        { $_ -in @('cp', 'copy') } {
            if ($wordToComplete -like '-*') {
                @('-bt', '-BufferSize', '-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('ls', 'list') } {
            if ($wordToComplete -like '-*') {
                @('--size', '--sizeasc', '--time', '--timeasc', '-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('rm', 'remove', 'del', 'delete') } {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'update' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'show' {
            if ($wordToComplete -like '-*') {
                @('--license', '--modelfile', '--parameters', '--system', '--template', '-v', '--verbose', '-d') |
                    Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        { $_ -in @('run', 'chat') } {
            if ($wordToComplete -like '-*') {
                @('-d', '--verbose', '--no-wordwrap', '--format', '--keepalive', '--dimensions', '--think', '--hide-thinking', '--insecure', '--truncate') |
                    Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'ps' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
        }

        'load' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        'unload' {
            if ($wordToComplete -like '-*') {
                @('-d') | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterName', $_)
                }
            }
            else {
                $models | Where-Object { $_ -like ""$wordToComplete*"" } | ForEach-Object {
                    [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', ""Model: $_"")
                }
            }
        }

        default {
            # No completion
        }
    }
}

# osync PowerShell completion - END";
        }
    }
    internal class Program
    {
        static string[] ReorderArguments(string[] args)
        {
            if (args.Length < 2) return args;

            // Commands that have optional positional model/pattern arguments
            string[] commandsWithPositional = { "show", "pull", "ls", "list", "rm", "remove", "del", "delete", "update", "run", "chat", "ps", "load", "unload" };

            string command = args[0];
            if (!commandsWithPositional.Contains(command.ToLower())) return args;

            // Find the first unlabeled argument (doesn't start with - or --)
            int firstUnlabeledIndex = -1;
            for (int i = 1; i < args.Length; i++)
            {
                if (!args[i].StartsWith("-") && !args[i].StartsWith("http"))
                {
                    // Skip if it's a value for a previous flag
                    if (i > 1 && (args[i - 1] == "-d" || args[i - 1] == "-bt" || args[i - 1] == "-BufferSize" ||
                                  args[i - 1] == "--format" || args[i - 1] == "--keepalive" ||
                                  args[i - 1] == "--dimensions" || args[i - 1] == "--think"))
                        continue;

                    firstUnlabeledIndex = i;
                    break;
                }
            }

            // If we found an unlabeled argument that's not at position 1, move it
            if (firstUnlabeledIndex > 1)
            {
                var reordered = new List<string> { args[0], args[firstUnlabeledIndex] };
                for (int i = 1; i < args.Length; i++)
                {
                    if (i != firstUnlabeledIndex)
                        reordered.Add(args[i]);
                }
                return reordered.ToArray();
            }

            return args;
        }

        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine($"osync v{OsyncProgram.GetAppVersion()}");
                System.Console.WriteLine();
                Console.ResetColors();

                // Reorder arguments for PowerArgs compatibility on Linux
                // PowerArgs has issues on Linux when flags come before positional arguments
                args = ReorderArguments(args);

                // If no arguments provided, enter interactive REPL mode
                if (args.Length == 0)
                {
                    OsyncProgram.isInteractiveMode = true;
                    Args.InvokeAction<OsyncProgram>(args);
                }
                else
                {
                    OsyncProgram.isInteractiveMode = false;
                    Args.InvokeAction<OsyncProgram>(args);
                }
            }
            catch (ArgException ex)
            {
                Console.WriteLine(ex.Message);
                Console.WriteLine("Use 'osync -h' or 'osync -?' for usage information.");
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
                string candidate = context.CompletionCandidate ?? "";

                // Try to find matches starting with the candidate
                var match = from m in models where m.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) select m;

                // If no matches, try alternative matching strategies
                if (match.Count() == 0)
                {
                    if (!candidate.Contains(":"))
                    {
                        // Case 1: candidate has no colon - try matching the part after ':' in model names
                        // This helps with Linux readline which treats ':' as a word boundary
                        match = from m in models
                                where m.Contains(":") &&
                                      m.Substring(m.IndexOf(':') + 1).StartsWith(candidate, StringComparison.OrdinalIgnoreCase)
                                select m;
                    }
                    else if (candidate.EndsWith(":"))
                    {
                        // Case 2: candidate ends with ':' (e.g., "qwen3:") - show all tags for that model
                        // This happens when user types model name and colon, wanting to see available tags
                        match = from m in models where m.StartsWith(candidate, StringComparison.OrdinalIgnoreCase) select m;
                    }
                }

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
                    if (commonChars == candidate.Length)
                    {
                        // Clear any previous options before displaying new ones
                        ClearPreviousOptions();

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
