using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;

namespace osync
{
    // Multiline input mode enum
    public enum MultilineMode
    {
        None,
        UserMessage,
        SystemMessage
    }

    // Chat message DTO
    public class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("thinking")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Thinking { get; set; }

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<byte[]>? Images { get; set; }
    }

    // Chat request DTO
    public class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = new();

        [JsonPropertyName("stream")]
        public bool Stream { get; set; } = true;

        [JsonPropertyName("format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Format { get; set; }

        [JsonPropertyName("keep_alive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? KeepAlive { get; set; }

        [JsonPropertyName("options")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, object>? Options { get; set; }

        [JsonPropertyName("think")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Think { get; set; }

        [JsonPropertyName("truncate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Truncate { get; set; }
    }

    // Chat response DTO
    public class ChatResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();

        [JsonPropertyName("done")]
        public bool Done { get; set; }

        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; set; }

        [JsonPropertyName("total_duration")]
        public long? TotalDuration { get; set; }

        [JsonPropertyName("load_duration")]
        public long? LoadDuration { get; set; }

        [JsonPropertyName("prompt_eval_count")]
        public int? PromptEvalCount { get; set; }

        [JsonPropertyName("prompt_eval_duration")]
        public long? PromptEvalDuration { get; set; }

        [JsonPropertyName("eval_count")]
        public int? EvalCount { get; set; }

        [JsonPropertyName("eval_duration")]
        public long? EvalDuration { get; set; }
    }

    // Session state for save/load
    public class SessionState
    {
        public string ModelName { get; set; } = string.Empty;
        public string SystemMessage { get; set; } = string.Empty;
        public List<ChatMessage> Messages { get; set; } = new();
        public Dictionary<string, object> Options { get; set; } = new();
        public bool WordWrap { get; set; } = true;
        public bool Verbose { get; set; }
        public bool HideThinking { get; set; }
        public string? Format { get; set; }
        public object? Think { get; set; }
        public bool? Truncate { get; set; }
        public DateTime SessionStartTime { get; set; }

        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public static SessionState? FromJson(string json)
        {
            return JsonSerializer.Deserialize<SessionState>(json);
        }
    }

    // Process status response DTO (from /api/ps)
    public class ProcessStatusResponse
    {
        [JsonPropertyName("models")]
        public List<RunningModel> Models { get; set; } = new();
    }

    public class RunningModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("digest")]
        public string Digest { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public ModelDetails Details { get; set; } = new();

        [JsonPropertyName("expires_at")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("size_vram")]
        public long SizeVram { get; set; }

        [JsonPropertyName("context_length")]
        public int ContextLength { get; set; }
    }

    public class ModelDetails
    {
        [JsonPropertyName("parent_model")]
        public string ParentModel { get; set; } = string.Empty;

        [JsonPropertyName("format")]
        public string Format { get; set; } = string.Empty;

        [JsonPropertyName("family")]
        public string Family { get; set; } = string.Empty;

        [JsonPropertyName("families")]
        public List<string> Families { get; set; } = new();

        [JsonPropertyName("parameter_size")]
        public string ParameterSize { get; set; } = string.Empty;

        [JsonPropertyName("quantization_level")]
        public string QuantizationLevel { get; set; } = string.Empty;
    }

    // Performance statistics tracker
    public class PerformanceStatistics
    {
        private readonly List<ChatResponse> _responses = new();

        public void AddResponse(ChatResponse response)
        {
            if (response != null && response.Done && response.TotalDuration.HasValue)
            {
                _responses.Add(response);
            }
        }

        public void Display()
        {
            if (_responses.Count == 0)
            {
                System.Console.WriteLine("No performance data available.");
                return;
            }

            var totalDurations = _responses
                .Select(r => r.TotalDuration!.Value / 1_000_000.0)
                .ToList();

            var loadDurations = _responses
                .Where(r => r.LoadDuration.HasValue)
                .Select(r => r.LoadDuration!.Value / 1_000_000.0)
                .ToList();

            var evalCounts = _responses
                .Where(r => r.EvalCount.HasValue)
                .Select(r => r.EvalCount!.Value)
                .ToList();

            var evalDurations = _responses
                .Where(r => r.EvalDuration.HasValue)
                .Select(r => r.EvalDuration!.Value / 1_000_000.0)
                .ToList();

            System.Console.WriteLine("\n=== Performance Statistics ===");
            System.Console.WriteLine($"Total requests: {_responses.Count}");

            if (totalDurations.Any())
            {
                System.Console.WriteLine($"\nTotal Duration (ms):");
                System.Console.WriteLine($"  Min: {totalDurations.Min():F2}");
                System.Console.WriteLine($"  Avg: {totalDurations.Average():F2}");
                System.Console.WriteLine($"  Max: {totalDurations.Max():F2}");
            }

            if (loadDurations.Any())
            {
                System.Console.WriteLine($"\nLoad Duration (ms):");
                System.Console.WriteLine($"  Min: {loadDurations.Min():F2}");
                System.Console.WriteLine($"  Avg: {loadDurations.Average():F2}");
                System.Console.WriteLine($"  Max: {loadDurations.Max():F2}");
            }

            if (evalCounts.Any())
            {
                System.Console.WriteLine($"\nTokens Generated:");
                System.Console.WriteLine($"  Min: {evalCounts.Min()}");
                System.Console.WriteLine($"  Avg: {evalCounts.Average():F0}");
                System.Console.WriteLine($"  Max: {evalCounts.Max()}");
            }

            if (evalDurations.Any() && evalCounts.Any())
            {
                var tokensPerSecond = _responses
                    .Where(r => r.EvalCount.HasValue && r.EvalDuration.HasValue && r.EvalDuration > 0)
                    .Select(r => r.EvalCount!.Value / (r.EvalDuration!.Value / 1_000_000_000.0))
                    .ToList();

                if (tokensPerSecond.Any())
                {
                    System.Console.WriteLine($"\nTokens/Second:");
                    System.Console.WriteLine($"  Min: {tokensPerSecond.Min():F2}");
                    System.Console.WriteLine($"  Avg: {tokensPerSecond.Average():F2}");
                    System.Console.WriteLine($"  Max: {tokensPerSecond.Max():F2}");
                }
            }

            System.Console.WriteLine("");
        }

        public void Clear()
        {
            _responses.Clear();
        }

        public int Count => _responses.Count;
    }
}
