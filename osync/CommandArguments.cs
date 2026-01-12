using PowerArgs;

namespace osync
{
    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class CopyArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Source model (e.g., llama3:latest or http://server:port/model:tag)")]
        public string Source { get; set; } = string.Empty;

        [ArgRequired, ArgPosition(2), ArgDescription("Destination (e.g., newmodel:tag or http://server:port/model:tag)")]
        public string Destination { get; set; } = string.Empty;

        [ArgDescription("Buffer size for remote-to-remote copy (default: 512MB, e.g., 1GB, 256MB)")]
        public string BufferSize { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class ListArgs
    {
        [ArgPosition(1), ArgDescription("Model pattern to filter (supports * wildcard, e.g., llama*, *:7b)")]
        public string Pattern { get; set; } = string.Empty;

        [ArgDescription("Remote server URL (e.g., http://192.168.1.100:11434)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;

        [ArgDescription("Sort by size descending"), ArgShortcut("--size")]
        public bool SortBySize { get; set; }

        [ArgDescription("Sort by size ascending"), ArgShortcut("--sizeasc")]
        public bool SortBySizeAsc { get; set; }

        [ArgDescription("Sort by creation time, newest first"), ArgShortcut("--time")]
        public bool SortByTime { get; set; }

        [ArgDescription("Sort by creation time, oldest first"), ArgShortcut("--timeasc")]
        public bool SortByTimeAsc { get; set; }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class RemoveArgs
    {
        [ArgPosition(1), ArgDescription("Model pattern to remove (supports * wildcard)")]
        public string Pattern { get; set; } = string.Empty;

        [ArgDescription("Remote server URL"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class RenameArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Source model name")]
        public string Source { get; set; } = string.Empty;

        [ArgRequired, ArgPosition(2), ArgDescription("New model name")]
        public string NewName { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class UpdateArgs
    {
        [ArgPosition(1), ArgDescription("Model pattern to update (supports * wildcard, default: *)")]
        public string Pattern { get; set; } = string.Empty;

        [ArgDescription("Remote server URL"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class PullArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Model name or HuggingFace URL (e.g., llama3:latest or https://huggingface.co/...)")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class ShowArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Model name to show information")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;

        [ArgDescription("Show license information"), ArgShortcut("--license")]
        public bool License { get; set; }

        [ArgDescription("Show modelfile"), ArgShortcut("--modelfile")]
        public bool Modelfile { get; set; }

        [ArgDescription("Show parameters"), ArgShortcut("--parameters")]
        public bool Parameters { get; set; }

        [ArgDescription("Show system prompt"), ArgShortcut("--system")]
        public bool System { get; set; }

        [ArgDescription("Show template"), ArgShortcut("--template")]
        public bool Template { get; set; }

        [ArgDescription("Show all information"), ArgShortcut("-v"), ArgShortcut("--verbose")]
        public bool Verbose { get; set; }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class RunArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Model name to run/chat with")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;

        [ArgDescription("Response format (json)"), ArgShortcut("--format")]
        public string Format { get; set; } = string.Empty;

        [ArgDescription("Keep alive duration (e.g., 5m, 1h, default: server default)"), ArgShortcut("--keepalive")]
        public string KeepAlive { get; set; } = string.Empty;

        [ArgDescription("Disable word wrap"), ArgShortcut("--nowordwrap")]
        public bool NoWordWrap { get; set; }

        [ArgDescription("Show verbose output"), ArgShortcut("--verbose")]
        public bool Verbose { get; set; }

        [ArgDescription("Image dimensions for vision models (e.g., 512)"), ArgShortcut("--dimensions")]
        public int? Dimensions { get; set; }

        [ArgDescription("Hide thinking process output"), ArgShortcut("--hidethinking")]
        public bool HideThinking { get; set; }

        [ArgDescription("Allow insecure connections"), ArgShortcut("--insecure")]
        public bool Insecure { get; set; }

        [ArgDescription("Enable extended thinking (reasoning) mode"), ArgShortcut("--think")]
        public string Think { get; set; } = string.Empty;

        [ArgDescription("Truncate long context (default: server setting)"), ArgShortcut("--truncate")]
        public bool? Truncate { get; set; }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class PsArgs
    {
        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d"), ArgPosition(1)]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class LoadArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Model name to load into memory")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class UnloadArgs
    {
        [ArgPosition(1), ArgDescription("Model name to unload from memory (optional - if not specified, unloads all models)")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d")]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class ManageArgs
    {
        [ArgDescription("Destination server URL (default: local)"), ArgShortcut("-d"), ArgPosition(1)]
        public string Destination { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class QcArgs
    {
        [ArgRequired, ArgDescription("Model name without tag"), ArgShortcut("-M")]
        public string ModelName { get; set; } = string.Empty;

        [ArgDescription("Remote server URL"), ArgShortcut("-D")]
        public string Destination { get; set; } = string.Empty;

        [ArgDescription("Results output file (default: modelname.qc.json)"), ArgShortcut("-O")]
        public string OutputFile { get; set; } = string.Empty;

        [ArgDescription("Base quantization tag for comparison (default: fp16, or existing base from results file)"), ArgShortcut("-B")]
        public string BaseTag { get; set; } = string.Empty;

        [ArgRequired, ArgDescription("Quantization tags to compare (comma-separated, supports wildcards e.g., Q4*,IQ*)"), ArgShortcut("-Q")]
        public string Quants { get; set; } = string.Empty;

        [ArgDescription("Test suite: v1base (50 questions), v1quick (v1base with 10 questions), v1code (50 questions coding-focused) or path to external JSON file (default: v1base)"), ArgShortcut("-T")]
        public string TestSuite { get; set; } = string.Empty;

        [ArgDescription("Model temperature (default: 0.0)"), ArgShortcut("-Te")]
        public double Temperature { get; set; } = 0.0;

        [ArgDescription("Model seed (default: 365)"), ArgShortcut("-S")]
        public int Seed { get; set; } = 365;

        [ArgDescription("Model top_p (default: 0.001)"), ArgShortcut("-To")]
        public double TopP { get; set; } = 0.001;

        [ArgDescription("Model top_k (default: -1)"), ArgShortcut("-Top")]
        public int TopK { get; set; } = -1;

        [ArgDescription("Model repeat_penalty"), ArgShortcut("-R")]
        public double? RepeatPenalty { get; set; }

        [ArgDescription("Model frequency_penalty"), ArgShortcut("-Fr")]
        public double? FrequencyPenalty { get; set; }

        [ArgDescription("Force re-run testing for quantizations already present in results file"), ArgShortcut("--force")]
        public bool Force { get; set; }

        [ArgDescription("Re-run judgment process for existing test results"), ArgShortcut("--rejudge")]
        public bool Rejudge { get; set; }

        [ArgDescription("Judge model for similarity scoring (local model name or http://host:port/model for remote)"), ArgShortcut("--judge")]
        public string Judge { get; set; } = string.Empty;

        [ArgDescription("Judge model for best answer determination (local model name or http://host:port/model for remote)"), ArgShortcut("--judgebest")]
        public string JudgeBest { get; set; } = string.Empty;

        [ArgDescription("Judge execution mode: serial (default) or parallel"), ArgShortcut("--mode")]
        public string JudgeMode { get; set; } = "serial";

        [ArgDescription("API timeout in seconds for testing and judgment calls (default: 600)"), ArgShortcut("--timeout")]
        public int Timeout { get; set; } = 600;

        [ArgDescription("Verbose output: show judgment details (question ID, score, reason)"), ArgShortcut("--verbose")]
        public bool Verbose { get; set; }

        [ArgDescription("Context length for judge model (0 = auto: test_ctx*2 + 2048)"), ArgShortcut("--judge-ctxsize")]
        public int JudgeCtxSize { get; set; } = 0;

        [ArgDescription("Pull models on-demand if not available, then remove after testing"), ArgShortcut("--ondemand")]
        public bool OnDemand { get; set; }

        [ArgDescription("Repository URL for the model source (saved in results file)"), ArgShortcut("--repo")]
        public string Repository { get; set; } = string.Empty;
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class QcViewArgs
    {
        [ArgRequired, ArgPosition(1), ArgDescription("Results file to view"), ArgShortcut("-F")]
        public string FileName { get; set; } = string.Empty;

        [ArgDescription("Output format: table, json, md, html, pdf (default: table)"), ArgShortcut("-Fo")]
        public string Format { get; set; } = "table";

        [ArgDescription("Output filename (default: console)"), ArgShortcut("-O")]
        public string OutputFile { get; set; } = string.Empty;

        [ArgDescription("Repository URL (overrides value from results file if specified)"), ArgShortcut("--repo")]
        public string Repository { get; set; } = string.Empty;

        [ArgDescription("Ignore judgment data, show only metrics-based scores"), ArgShortcut("--metricsonly")]
        public bool MetricsOnly { get; set; }
    }

    [ArgExceptionBehavior(ArgExceptionPolicy.StandardExceptionHandling)]
    public class VersionArgs
    {
        [ArgDescription("Show detailed version and environment information"), ArgShortcut("--verbose")]
        public bool Verbose { get; set; }
    }
}
