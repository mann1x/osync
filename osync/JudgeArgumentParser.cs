namespace osync
{
    /// <summary>
    /// Result of parsing a judge model argument string.
    /// </summary>
    public class JudgeParseResult
    {
        public bool IsCloud { get; init; }
        public ICloudJudgeProvider? CloudProvider { get; init; }
        public string? BaseUrl { get; init; }
        public string? ModelName { get; init; }
        public string? Error { get; init; }
        public string? KeySource { get; init; }
        public string? ApiVersion { get; init; }
        public bool Success { get; init; }
    }

    /// <summary>
    /// Shared parser for --judge and --judgebest arguments.
    /// Supports: local model, remote server, cloud provider (@provider[:token]/model).
    /// </summary>
    public static class JudgeArgumentParser
    {
        public static JudgeParseResult Parse(string argument, int timeout = 0)
        {
            // Check for cloud provider syntax (@provider[:token]/model)
            if (CloudJudgeProviderFactory.IsCloudProvider(argument))
            {
                var config = CloudJudgeProviderFactory.ParseArgument(argument);
                if (config == null)
                {
                    return new JudgeParseResult
                    {
                        Success = false,
                        Error = "Invalid cloud judge format. Expected @provider[:token]/model"
                    };
                }

                if (string.IsNullOrEmpty(config.ApiKey))
                {
                    var envVars = CloudJudgeProviderFactory.GetEnvVarsForProvider(config.ProviderName);
                    return new JudgeParseResult
                    {
                        Success = false,
                        Error = $"No API key found for {config.ProviderName}. Set {string.Join(" or ", envVars)} environment variable or provide key in command."
                    };
                }

                var provider = CloudJudgeProviderFactory.CreateProvider(config, timeout);
                if (provider == null)
                {
                    return new JudgeParseResult
                    {
                        Success = false,
                        Error = $"Failed to create cloud provider '{config.ProviderName}'"
                    };
                }

                if (!config.ApiKeyFromEnv)
                {
                    // A key on the command line stays in the shell history and is visible to other users in the process list
                    var envVars = CloudJudgeProviderFactory.GetEnvVarsForProvider(config.ProviderName);
                    var hint = envVars.Length > 0 ? $"; prefer {string.Join(" or ", envVars)}" : "";
                    Out.Warning($"the {config.ProviderName} API key is on the command line (shell history, process list){hint}.");
                }

                return new JudgeParseResult
                {
                    Success = true,
                    IsCloud = true,
                    CloudProvider = provider,
                    ModelName = config.ModelName,
                    KeySource = config.ApiKeyFromEnv ? "env" : "cmd",
                    ApiVersion = provider.GetApiVersion()
                };
            }

            // Parse server/model from various URL formats
            string? serverPart = null;
            string modelPart = argument;

            if (argument.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                argument.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(argument);
                serverPart = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                modelPart = uri.AbsolutePath.TrimStart('/');
            }
            else
            {
                var slashIndex = argument.IndexOf('/');
                if (slashIndex > 0)
                {
                    var possibleServer = argument.Substring(0, slashIndex);
                    if (OsyncProgram.LooksLikeRemoteServer(possibleServer) ||
                        OsyncProgram.LooksLikeRemoteServer(possibleServer + "/"))
                    {
                        serverPart = OsyncProgram.NormalizeServerUrl(possibleServer);
                        modelPart = argument.Substring(slashIndex + 1).TrimStart('/');
                    }
                }
            }

            // Judges without a server use the local server (XOLLAMA_HOST / OLLAMA_HOST / localhost), never -d
            var baseUrl = serverPart ?? OllamaServer.LocalUrl;
            if (!modelPart.Contains(':'))
            {
                modelPart += ":latest";
            }

            return new JudgeParseResult
            {
                Success = true,
                IsCloud = false,
                BaseUrl = baseUrl,
                ModelName = modelPart
            };
        }
    }
}
