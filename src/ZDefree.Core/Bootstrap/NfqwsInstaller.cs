using System.Runtime.InteropServices;

namespace ZDefree.Core.Bootstrap;

public sealed record NfqwsOptions
{
    public required string TargetDir { get; init; }
    public NfqwsArch Arch { get; init; } = NfqwsArch.Auto;
    public IProgress<BootstrapProgress>? Progress { get; init; }
}

public sealed record NfqwsInstallResult(
    string TargetPath,
    string ArchSubdir,
    string ReleaseTag,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Installs <c>nfqws</c> from <c>bol-van/zapret</c>'s latest release. Pulls the
/// openwrt-embedded tarball (small, ships pre-built binaries for the common
/// Linux archs) and extracts just the matching <c>linux-&lt;arch&gt;/nfqws</c>.
///
/// On Linux, the extracted binary is <c>chmod 0755</c>'d. On other hosts the
/// file is still written (useful for cross-build / staging) but the permission
/// bit is left as-is.
/// </summary>
public sealed class NfqwsInstaller
{
    private const string Owner = "bol-van";
    private const string Repo  = "zapret";

    private readonly GitHubClient _gh;

    public NfqwsInstaller(GitHubClient gh)
    {
        _gh = gh;
    }

    public async Task<NfqwsInstallResult> InstallAsync(NfqwsOptions opts, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        string subdir = NfqwsArchHelper.ResolveSubdir(opts.Arch);

        Report(opts, $"Fetching {Owner}/{Repo} latest release");
        var release = await _gh.GetLatestReleaseAsync(Owner, Repo, ct);

        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Contains("openwrt-embedded", StringComparison.OrdinalIgnoreCase) &&
            a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase));

        if (asset is null)
        {
            throw new InvalidOperationException(
                $"No openwrt-embedded tarball found in {Owner}/{Repo} release {release.TagName}.");
        }

        string tempTar = Path.Combine(Path.GetTempPath(), $"zapret-{Guid.NewGuid():N}.tar.gz");
        try
        {
            Report(opts, $"Downloading {asset.Name} ({release.TagName})");
            await _gh.DownloadToFileAsync(asset.DownloadUrl, tempTar, asset.Sha256, progress: null, ct);

            string destPath = Path.Combine(opts.TargetDir, "bin", "nfqws");
            string entrySuffix = $"binaries/{subdir}/nfqws";

            Report(opts, $"Extracting {entrySuffix}");
            await using var src = File.OpenRead(tempTar);
            bool found = await NfqwsTarExtractor.ExtractSingleAsync(src, entrySuffix, destPath, ct);
            if (!found)
            {
                throw new InvalidOperationException(
                    $"Entry ending in '{entrySuffix}' not found in {asset.Name}. Available archs may differ in this release.");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    File.SetUnixFileMode(destPath,
                        UnixFileMode.UserRead    | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead   | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead   | UnixFileMode.OtherExecute);
                }
                catch (Exception ex)
                {
                    warnings.Add($"chmod 0755 failed for {destPath}: {ex.Message}");
                }
            }

            return new NfqwsInstallResult(destPath, subdir, release.TagName, warnings);
        }
        finally
        {
            try { File.Delete(tempTar); } catch { }
        }
    }

    private static void Report(NfqwsOptions opts, string stage, int? pct = null)
    {
        opts.Progress?.Report(new BootstrapProgress("nfqws", stage, pct));
    }
}
