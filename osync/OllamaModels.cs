namespace osync
{
    /// <summary>
    /// Ollama manifest config
    /// </summary>
    public class Config
    {
        public string mediaType { get; set; } = string.Empty;
        public string digest { get; set; } = string.Empty;
        public int size { get; set; }
    }

    /// <summary>
    /// Ollama manifest layer
    /// </summary>
    public class Layer
    {
        public string mediaType { get; set; } = string.Empty;
        public string digest { get; set; } = string.Empty;
        public long size { get; set; }
        public string from { get; set; } = string.Empty;
    }

    /// <summary>
    /// Ollama model manifest
    /// </summary>
    public class RootManifest
    {
        public int schemaVersion { get; set; }
        public string mediaType { get; set; } = string.Empty;
        public Config? config { get; set; }
        public List<Layer>? layers { get; set; }
    }

    /// <summary>
    /// Ollama API status response
    /// </summary>
    public class RootStatus
    {
        public string status { get; set; } = string.Empty;
    }

    /// <summary>
    /// Ollama version response
    /// </summary>
    public class RootVersion
    {
        public string version { get; set; } = string.Empty;
    }

    /// <summary>
    /// Local model information
    /// </summary>
    public class LocalModelInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime ModifiedAt { get; set; }
    }

    /// <summary>
    /// Ollama /api/tags response
    /// </summary>
    public class OllamaModelsResponse
    {
        public List<OllamaModel>? models { get; set; }
    }

    /// <summary>
    /// Ollama model in /api/tags response
    /// </summary>
    public class OllamaModel
    {
        public string name { get; set; } = string.Empty;
        public string model { get; set; } = string.Empty;
        public DateTime modified_at { get; set; }
        public long size { get; set; }
        public string digest { get; set; } = string.Empty;
    }
}
