using System.Text.Json;
using System.Text.Json.Nodes;

namespace osync
{
    /// <summary>
    /// The on-disk model store of a server (OLLAMA_MODELS): where a model's manifest is and what it holds.
    /// Two layouts, both read the way Ollama itself reads them (manifest/paths.go of v0.40.0):
    ///   manifests-v2/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;  Ollama/xOllama 0.40+: a symlink (or a copy) of the manifest blob;
    ///                                                  the public registry is "ollama.com", ':' in a host is "%3A"
    ///   manifests/&lt;host&gt;/&lt;namespace&gt;/&lt;model&gt;/&lt;tag&gt;     older servers: the manifest itself; 0.40 keeps it only as a
    ///                                                  downgrade anchor, so a v2 entry always wins
    /// From 0.40 a manifest can also be a list with one child manifest per runner (ggml, llamacpp, mlx), stored as blobs.
    /// </summary>
    public static class ModelStore
    {
        public const string LegacyDir = "manifests";
        public const string V2Dir = "manifests-v2";
        public const string ManifestListMediaType = "application/vnd.ollama.manifest.list.v2+json";

        private const string LegacyPublicHost = "registry.ollama.ai";
        private const string V2PublicHost = "ollama.com";

        /// <summary>A model name split like Ollama's model.ParseName: host/namespace/model:tag with the default parts filled in.</summary>
        public readonly record struct ModelName(string Host, string Namespace, string Model, string Tag)
        {
            /// <summary>The shortest name Ollama shows for it (model:tag, namespace/model:tag, or host/namespace/model:tag).</summary>
            public string Display => IsPublicHost(Host)
                ? (Namespace == "library" ? $"{Model}:{Tag}" : $"{Namespace}/{Model}:{Tag}")
                : $"{Host}/{Namespace}/{Model}:{Tag}";
        }

        public static bool IsPublicHost(string host) =>
            host.Equals(LegacyPublicHost, StringComparison.OrdinalIgnoreCase) || host.Equals(V2PublicHost, StringComparison.OrdinalIgnoreCase);

        /// <summary>Parses "model", "ns/model", "host/ns/model" with an optional ":tag" (a ':' before the last '/' is a port).</summary>
        public static ModelName? Parse(string name)
        {
            name = name.Trim();
            string tag = "latest";
            int colon = name.LastIndexOf(':');
            if (colon > name.LastIndexOf('/'))
            {
                tag = name[(colon + 1)..];
                name = name[..colon];
            }
            var parts = name.Split('/');
            if (tag.Length == 0 || parts.Any(p => p.Length == 0 || p == "." || p == ".."))
                return null;
            return parts.Length switch
            {
                1 => new ModelName(LegacyPublicHost, "library", parts[0], tag),
                2 => new ModelName(LegacyPublicHost, parts[0], parts[1], tag),
                3 => new ModelName(parts[0], parts[1], parts[2], tag),
                _ => null
            };
        }

        /// <summary>
        /// Where the manifest of a model can be, in the order Ollama looks: the v2 entry, then the legacy paths
        /// (both public host spellings).
        /// </summary>
        public static IReadOnlyList<string> ManifestCandidates(string modelsDir, string model)
        {
            if (Parse(model) is not { } n)
                return [];
            var v2Host = IsPublicHost(n.Host) ? V2PublicHost : n.Host;
            var paths = new List<string> { Path.Combine(modelsDir, V2Dir, v2Host.Replace(":", "%3A"), n.Namespace, n.Model, n.Tag) };
            var legacyHosts = IsPublicHost(n.Host) ? new[] { n.Host, n.Host.Equals(LegacyPublicHost, StringComparison.OrdinalIgnoreCase) ? V2PublicHost : LegacyPublicHost } : [n.Host];
            foreach (var host in legacyHosts)
            {
                // A Windows path cannot hold "host:port"
                if (OperatingSystem.IsWindows() && host.Contains(':'))
                    continue;
                paths.Add(Path.Combine(modelsDir, LegacyDir, host, n.Namespace, n.Model, n.Tag));
            }
            return paths;
        }

        /// <summary>
        /// The manifest file of a model, or null. File.Exists follows symlinks, so a dangling v2 link (its blob removed
        /// by an older server) falls through to the legacy manifest, as in Ollama.
        /// </summary>
        public static string? FindManifest(string modelsDir, string model) =>
            ManifestCandidates(modelsDir, model).FirstOrDefault(File.Exists);

        /// <summary>Every named manifest of the store: (name, path), v2 entries first, each name once.</summary>
        public static List<(ModelName Name, string Path)> EnumerateManifests(string modelsDir)
        {
            var found = new List<(ModelName, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (dir, v2) in new[] { (V2Dir, true), (LegacyDir, false) })
            {
                var root = Path.Combine(modelsDir, dir);
                if (!Directory.Exists(root))
                    continue;
                foreach (var hostDir in Directory.GetDirectories(root))
                foreach (var nsDir in Directory.GetDirectories(hostDir))
                foreach (var modelDir in Directory.GetDirectories(nsDir))
                foreach (var tagFile in Directory.GetFiles(modelDir))
                {
                    var tag = Path.GetFileName(tagFile);
                    // Temporary files of a manifest being written
                    if (tag.StartsWith('.') || !File.Exists(tagFile))
                        continue;
                    var host = Path.GetFileName(hostDir);
                    if (v2)
                        host = host.Replace("%3A", ":").Replace("%3a", ":");
                    var name = new ModelName(IsPublicHost(host) ? LegacyPublicHost : host,
                        Path.GetFileName(nsDir), Path.GetFileName(modelDir), tag);
                    // 0.40 names the store's converted builds "llamacpp:<digest>" (legacy shadows kept for downgrades)
                    if (!v2 && name.Namespace == "library" && name.Model == "llamacpp" && IsPublicHost(host))
                        continue;
                    if (seen.Add(name.Display))
                        found.Add((name, tagFile));
                }
            }
            return found;
        }

        public static string BlobPath(string modelsDir, string digest) =>
            Path.Combine(modelsDir, "blobs", digest.Replace(':', '-'));

        /// <summary>
        /// The bytes of the model manifest to copy: the manifest itself, or for a manifest list the child osync copies
        /// (<see cref="SelectChild"/>), read from its blob. Throws when a list child is not in the store.
        /// </summary>
        public static byte[] ReadModelManifest(string modelsDir, string manifestPath, out string? runner)
        {
            runner = null;
            var bytes = File.ReadAllBytes(manifestPath);
            if (JsonNode.Parse(bytes) is not JsonObject root || (string?)root["mediaType"] != ManifestListMediaType)
                return bytes;

            var child = SelectChild(root)
                ?? throw new InvalidOperationException("the manifest list has no child manifest");
            runner = child.Runner;
            var blob = BlobPath(modelsDir, child.Digest);
            if (!File.Exists(blob))
                throw new InvalidOperationException($"the {child.Runner} build of the model is not in the models directory");
            return File.ReadAllBytes(blob);
        }

        /// <summary>
        /// The child of a manifest list that a copy installs: the ggml build (the original GGUF, which every server
        /// version loads and 0.40 converts again on its first load), else the llama.cpp build, else the first one.
        /// </summary>
        public static (string Digest, string Runner)? SelectChild(JsonObject list)
        {
            if (list["manifests"] is not JsonArray children)
                return null;
            var entries = children.OfType<JsonObject>()
                .Select(c => (Digest: (string?)c["digest"] ?? "", Runner: ((string?)c["runner"] ?? "").ToLowerInvariant()))
                .Where(c => c.Digest.Length > 0)
                .ToList();
            foreach (var runner in new[] { "ggml", "llamacpp" })
                if (entries.FirstOrDefault(e => e.Runner == runner) is { Digest.Length: > 0 } match)
                    return match;
            return entries.Count > 0 ? entries[0] : null;
        }
    }
}
