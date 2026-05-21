using System.Runtime.InteropServices;
using System.Text.Json;

namespace ZDefree.Core.Bootstrap;

public sealed class BootstrapRunner : IDisposable
{
    private const string WinBundleOwner = "bol-van";
    private const string WinBundleRepo  = "zapret-win-bundle";

    private const string WinDivertOwner = "basil00";
    private const string WinDivertRepo  = "WinDivert";

    private const string PatternsOwner  = "Flowseal";
    private const string PatternsRepo   = "zapret-discord-youtube";
    private const string PatternsPath   = "bin";

    private readonly GitHubClient _gh;
    private readonly bool _ownsClient;

    public BootstrapRunner(GitHubClient? client = null)
    {
        _gh = client ?? new GitHubClient();
        _ownsClient = client is null;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _gh.Dispose();
        }
    }

    public async Task<BootstrapResult> RunAsync(BootstrapOptions options, CancellationToken ct = default)
    {
        Directory.CreateDirectory(options.TargetDir);

        var result = new BootstrapResult { TargetDir = options.TargetDir };

        if (options.DownloadWinws)
        {
            await InstallWinwsAsync(options, result, ct);
        }

        if (options.DownloadWinDivert)
        {
            await InstallWinDivertAsync(options, result, ct);
        }

        if (options.DownloadPatterns)
        {
            await InstallPatternsAsync(options, result, ct);
        }

        await WriteVersionFileAsync(options, result, ct);

        return result;
    }

    internal static string ResolveWinwsSubpath(WinwsArch arch)
    {
        var effective = arch == WinwsArch.Auto ? DetectArch() : arch;
        return effective switch
        {
            WinwsArch.X64   => "zapret-winws",
            WinwsArch.X86   => "win7",
            WinwsArch.Arm64 => "arm64",
            _ => "zapret-winws",
        };
    }

    internal static WinwsArch DetectArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => WinwsArch.Arm64,
            Architecture.X86   => WinwsArch.X86,
            Architecture.X64   => WinwsArch.X64,
            _ => WinwsArch.X64,
        };
    }

    private async Task InstallWinwsAsync(BootstrapOptions opt, BootstrapResult result, CancellationToken ct)
    {
        string subpath = ResolveWinwsSubpath(opt.Arch);
        Report(opt, "winws", $"Listing {WinBundleOwner}/{WinBundleRepo}/{subpath}");

        var files = await _gh.ListDirectoryAsync(WinBundleOwner, WinBundleRepo, subpath, ct: ct);
        var wanted = files.Where(IsRelevantWinwsFile).ToList();

        if (wanted.Count == 0)
        {
            result.Warnings.Add($"No files matched in {subpath} of {WinBundleOwner}/{WinBundleRepo}.");
            return;
        }

        string binDir = Path.Combine(opt.TargetDir, "bin");
        Directory.CreateDirectory(binDir);

        int i = 0;
        foreach (var f in wanted)
        {
            i++;
            int pct = (int)(100.0 * i / wanted.Count);
            Report(opt, "winws", $"Downloading {f.Name} ({i}/{wanted.Count})", pct);

            if (string.IsNullOrEmpty(f.DownloadUrl))
            {
                result.Warnings.Add($"Skipped {f.Name}: no download_url from GitHub API.");
                continue;
            }

            string dest = Path.Combine(binDir, f.Name);
            await _gh.DownloadToFileAsync(f.DownloadUrl, dest, expectedSha256: null, progress: null, ct);
            result.InstalledFiles.Add(Path.GetRelativePath(opt.TargetDir, dest));
        }

        result.WinwsVersion = await TryGetLatestCommitShaAsync(WinBundleOwner, WinBundleRepo, ct);
    }

    internal static bool IsRelevantWinwsFile(GitHubFileInfo f)
    {
        if (!string.Equals(f.Type, "file", StringComparison.OrdinalIgnoreCase)) return false;
        string n = f.Name.ToLowerInvariant();
        if (n.EndsWith(".exe")) return true;
        if (n.EndsWith(".dll")) return true;
        if (n.EndsWith(".sys")) return true;
        if (n.EndsWith(".bin")) return true;
        if (n.EndsWith(".cat")) return true;
        if (n.Equals("readme.md") || n.Equals("license") || n.Equals("license.txt")) return true;
        return false;
    }

    private async Task InstallWinDivertAsync(BootstrapOptions opt, BootstrapResult result, CancellationToken ct)
    {
        Report(opt, "windivert", "Fetching latest release");
        var release = await _gh.GetLatestReleaseAsync(WinDivertOwner, WinDivertRepo, ct);

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("WinDivert", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            result.Warnings.Add($"WinDivert: no .zip asset found in release {release.TagName}.");
            return;
        }

        string tempZip = Path.Combine(Path.GetTempPath(), $"windivert-{Guid.NewGuid():N}.zip");
        try
        {
            Report(opt, "windivert", $"Downloading {asset.Name} ({release.TagName})");
            await _gh.DownloadToFileAsync(asset.DownloadUrl, tempZip, asset.Sha256, progress: null, ct);

            string dest = Path.Combine(opt.TargetDir, "vendor", "windivert");
            Directory.CreateDirectory(dest);

            Report(opt, "windivert", "Extracting");
            ZipExtractor.ExtractMatchingTo(tempZip, dest, IsRelevantWinDivertFile, flatten: true);

            foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
            {
                result.InstalledFiles.Add(Path.GetRelativePath(opt.TargetDir, f));
            }

            result.WinDivertVersion = release.TagName;
        }
        finally
        {
            try { File.Delete(tempZip); } catch { }
        }
    }

    // WinDivert release zip layout: WinDivert-X.Y.Z-A/{x86,x64}/<files> + top-level LICENSE.
    // We only want the x64 user-mode files and the kernel driver (which is x64-only).
    internal static bool IsRelevantWinDivertFile(string fullName)
    {
        string lower = fullName.Replace('\\', '/').ToLowerInvariant();
        string name = Path.GetFileName(lower);

        bool inX64 = lower.Contains("/x64/") || lower.StartsWith("x64/");

        if (name == "windivert.dll")   return inX64;
        if (name == "windivert.lib")   return inX64;
        if (name == "windivert64.sys") return true;
        if (name == "license" || name == "license.txt") return true;

        return false;
    }

    private async Task InstallPatternsAsync(BootstrapOptions opt, BootstrapResult result, CancellationToken ct)
    {
        Report(opt, "patterns", $"Listing {PatternsOwner}/{PatternsRepo}/{PatternsPath}");

        List<GitHubFileInfo> files;
        try
        {
            files = await _gh.ListDirectoryAsync(PatternsOwner, PatternsRepo, PatternsPath, ct: ct);
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Patterns: failed to list {PatternsOwner}/{PatternsRepo}/{PatternsPath}: {ex.Message}");
            return;
        }

        var binFiles = files
            .Where(f => string.Equals(f.Type, "file", StringComparison.OrdinalIgnoreCase))
            .Where(f => f.Name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (binFiles.Count == 0)
        {
            result.Warnings.Add($"Patterns: no .bin files found in {PatternsOwner}/{PatternsRepo}/{PatternsPath}.");
            return;
        }

        string binDir = Path.Combine(opt.TargetDir, "bin");
        Directory.CreateDirectory(binDir);

        int i = 0;
        foreach (var f in binFiles)
        {
            i++;
            int pct = (int)(100.0 * i / binFiles.Count);
            Report(opt, "patterns", $"Downloading {f.Name} ({i}/{binFiles.Count})", pct);

            if (string.IsNullOrEmpty(f.DownloadUrl))
            {
                result.Warnings.Add($"Patterns: skipped {f.Name} (no download_url).");
                continue;
            }

            string dest = Path.Combine(binDir, f.Name);
            if (File.Exists(dest)) continue;

            await _gh.DownloadToFileAsync(f.DownloadUrl, dest, expectedSha256: null, progress: null, ct);
            result.InstalledFiles.Add(Path.GetRelativePath(opt.TargetDir, dest));
            result.PatternsInstalled++;
        }
    }

    private async Task<string?> TryGetLatestCommitShaAsync(string owner, string repo, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{owner}/{repo}/commits/master";
            using var resp = await _gh.GetFollowingAllowedRedirectsAsync(url, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("sha", out var sha) ? sha.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task WriteVersionFileAsync(BootstrapOptions opt, BootstrapResult result, CancellationToken ct)
    {
        var data = new
        {
            distribution = "ZDefree",
            generated_at = DateTimeOffset.UtcNow.ToString("o"),
            winws = result.WinwsVersion,
            windivert = result.WinDivertVersion,
        };

        string json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        string path = Path.Combine(opt.TargetDir, "bin", "VERSION");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json, ct);
        result.InstalledFiles.Add(Path.GetRelativePath(opt.TargetDir, path));
    }

    private static void Report(BootstrapOptions opt, string component, string stage, int? pct = null)
    {
        opt.Progress?.Report(new BootstrapProgress(component, stage, pct));
    }
}
