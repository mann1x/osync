using System.Net.Http.Headers;

namespace osync
{
    /// <summary>
    /// Requests to HuggingFace carry the HF_TOKEN environment variable as a bearer token when it is set: higher rate
    /// limits, and access to private or gated repositories. Only HuggingFace hosts get it; the clients these requests
    /// go through are also used for Ollama servers, so the token is added per request, never as a default header.
    /// </summary>
    internal static class HuggingFaceAuth
    {
        public static string? Token => Environment.GetEnvironmentVariable("HF_TOKEN") is { Length: > 0 } token ? token.Trim() : null;

        /// <summary>A GET request for <paramref name="url"/>, authorized when it is a HuggingFace URL and HF_TOKEN is set.</summary>
        public static HttpRequestMessage Get(string url, string? token = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            token ??= Token;
            if (token != null && IsHuggingFaceHost(request.RequestUri!))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return request;
        }

        public static bool IsHuggingFaceHost(Uri uri) =>
            uri.Scheme == Uri.UriSchemeHttps &&
            (uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.Equals("hf.co", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".huggingface.co", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".hf.co", StringComparison.OrdinalIgnoreCase));
    }
}
