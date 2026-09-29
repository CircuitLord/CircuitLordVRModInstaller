using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace BigWalkVRInstaller.Services
{
    public static class RepoClient
    {
        static readonly HttpClient Http;

        static RepoClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            Http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("CircuitLordVRModInstaller");
        }

        public static async Task<Manifest> FetchManifest(string url)
        {
            RequireHttps(url);
            // cache buster, raw.githubusercontent holds manifests for a few minutes
            var bust = url + (url.Contains("?") ? "&" : "?") + "t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var json = await Http.GetStringAsync(bust);
            var manifest = JsonUtil.Deserialize<Manifest>(json);
            if (manifest?.schemaVersion != 2 || manifest.bepinex == null || manifest.mods == null) throw new Exception("invalid manifest schema");
            return manifest;
        }

        public static async Task<byte[]> Download(string url, string expectedSha256, IProgress<double> progress = null)
        {
            RequireHttps(url);
            ValidateSha256(expectedSha256);

            byte[] bytes;
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                using (var stream = await response.Content.ReadAsStreamAsync())
                using (var buffer = new MemoryStream())
                {
                    var chunk = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(chunk, 0, chunk.Length)) > 0)
                    {
                        buffer.Write(chunk, 0, read);
                        if (total > 0) progress?.Report((double)buffer.Length / total);
                    }
                    bytes = buffer.ToArray();
                }
            }

            var actual = Sha256(bytes);
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"sha256 mismatch for {url}");
            return bytes;
        }

        static void RequireHttps(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                throw new Exception($"URL must use HTTPS: {url}");
        }

        static void ValidateSha256(string value)
        {
            if (value == null || value.Length != 64) throw new Exception("download requires a SHA-256 hash");
            foreach (var c in value)
                if (!Uri.IsHexDigit(c)) throw new Exception("download requires a valid SHA-256 hash");
        }

        public static string Sha256(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
