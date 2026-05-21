using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZDefree.Core.Bootstrap;

public sealed class GitHubFileInfo
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public required long Size { get; init; }
    public string? Sha { get; init; }
    public string? Type { get; init; }
}

public sealed class GitHubReleaseAsset
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public required long Size { get; init; }
    public string? Sha256 { get; init; }
}

public sealed class GitHubRelease
{
    public required string TagName { get; init; }
    public required List<GitHubReleaseAsset> Assets { get; init; }
}

public sealed class GitHubClient : IDisposable
{
    internal static readonly string[] AllowedHosts =
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "raw.githubusercontent.com",
        "codeload.github.com",
    };

    private const int MaxRedirects = 5;

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GitHubClient(HttpClient? http = null)
    {
        if (http is null)
        {
            var handler = new HttpClientHandler
            {
                CheckCertificateRevocationList = true,
                AllowAutoRedirect = false,
            };
            _http = new HttpClient(handler);
            _ownsClient = true;
        }
        else
        {
            _http = http;
            _ownsClient = false;
        }

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZDefree-Bootstrap/0.1");
        }
        if (_http.Timeout == TimeSpan.FromSeconds(100))
        {
            _http.Timeout = TimeSpan.FromMinutes(3);
        }
    }

    public async Task<List<GitHubFileInfo>> ListDirectoryAsync(string owner, string repo, string path, string? branch = null, CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{path}";
        if (!string.IsNullOrEmpty(branch))
        {
            url += $"?ref={Uri.EscapeDataString(branch)}";
        }

        using var resp = await GetFollowingAllowedRedirectsAsync(url, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                $"Expected an array from {url}, got {doc.RootElement.ValueKind}. " +
                $"Is the path a directory?");
        }

        var result = new List<GitHubFileInfo>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            result.Add(new GitHubFileInfo
            {
                Name = item.GetProperty("name").GetString() ?? "",
                DownloadUrl = item.TryGetProperty("download_url", out var d) ? d.GetString() ?? "" : "",
                Size = item.GetProperty("size").GetInt64(),
                Sha = item.TryGetProperty("sha", out var s) ? s.GetString() : null,
                Type = item.TryGetProperty("type", out var t) ? t.GetString() : null,
            });
        }
        return result;
    }

    public async Task<GitHubRelease> GetLatestReleaseAsync(string owner, string repo, CancellationToken ct = default)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        using var resp = await GetFollowingAllowedRedirectsAsync(url, ct);
        string body = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var assets = new List<GitHubReleaseAsset>();
        if (doc.RootElement.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                string? sha = null;
                if (a.TryGetProperty("digest", out var dig))
                {
                    string? raw = dig.GetString();
                    if (!string.IsNullOrEmpty(raw) && raw.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    {
                        sha = raw.Substring("sha256:".Length).Trim();
                    }
                }
                assets.Add(new GitHubReleaseAsset
                {
                    Name = a.GetProperty("name").GetString() ?? "",
                    DownloadUrl = a.GetProperty("browser_download_url").GetString() ?? "",
                    Size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0,
                    Sha256 = sha,
                });
            }
        }

        return new GitHubRelease
        {
            TagName = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "",
            Assets = assets,
        };
    }

    public async Task DownloadToFileAsync(string url, string destPath, string? expectedSha256 = null,
        IProgress<long>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        string tempPath = destPath + ".part";
        try
        {
            using (var resp = await GetFollowingAllowedRedirectsAsync(url, ct))
            {
                long? total = resp.Content.Headers.ContentLength;
                await using var src = await resp.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(tempPath);

                var buf = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    progress?.Report(read);
                }
            }

            if (expectedSha256 is not null)
            {
                string actual = await ComputeSha256Async(tempPath, ct);
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new InvalidDataException(
                        $"SHA-256 mismatch for {url}: " +
                        $"expected {expectedSha256[..12]}…, got {actual[..12]}…");
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tempPath, destPath);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }

    internal async Task<HttpResponseMessage> GetFollowingAllowedRedirectsAsync(string initialUrl, CancellationToken ct)
    {
        string currentUrl = initialUrl;
        HttpResponseMessage? resp = null;
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!IsAllowedHost(currentUrl))
            {
                resp?.Dispose();
                throw new InvalidOperationException(
                    $"Download blocked: host {SafeHost(currentUrl)} is not in the allowlist.");
            }

            resp?.Dispose();
            resp = await _http.GetAsync(currentUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!IsRedirect(resp.StatusCode))
            {
                resp.EnsureSuccessStatusCode();
                return resp;
            }

            var loc = resp.Headers.Location;
            if (loc is null)
            {
                resp.EnsureSuccessStatusCode();
                return resp;
            }

            var nextUri = loc.IsAbsoluteUri ? loc : new Uri(new Uri(currentUrl), loc);
            currentUrl = nextUri.ToString();
        }
        resp?.Dispose();
        throw new InvalidOperationException($"Too many redirects (>{MaxRedirects}) for {initialUrl}");
    }

    internal static bool IsAllowedHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        foreach (var host in AllowedHosts)
        {
            if (string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code == HttpStatusCode.MovedPermanently
        || code == HttpStatusCode.Found
        || code == HttpStatusCode.SeeOther
        || code == HttpStatusCode.TemporaryRedirect
        || code == HttpStatusCode.PermanentRedirect;

    private static string SafeHost(string url)
    {
        try { return new Uri(url).Host; } catch { return "<invalid>"; }
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        byte[] hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}
