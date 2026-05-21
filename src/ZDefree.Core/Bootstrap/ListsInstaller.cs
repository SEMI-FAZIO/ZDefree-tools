using System.Text;
using System.Text.Json;

namespace ZDefree.Core.Bootstrap;

public sealed record PackDefinition(
    string Name,
    IReadOnlyList<string> Keywords,
    ListKind Kind,
    string SourceFile);

public sealed record ListsOptions
{
    public required string TargetDir { get; init; }
    public IReadOnlyList<string>? Packs { get; init; }
    public bool ValidateOnly { get; init; } = false;
    public IProgress<BootstrapProgress>? Progress { get; init; }
}

public sealed record InstalledPack(
    string Name,
    string File,
    int LineCount,
    ListKind Kind);

public sealed record ListsInstallResult(
    string TargetDir,
    IReadOnlyList<InstalledPack> Packs,
    string? SourceSha,
    IReadOnlyList<string> Warnings);

public sealed class ListsInstaller
{
    private const string SourceOwner = "1andrevich";
    private const string SourceRepo  = "Re-filter-lists";

    // Domain pack definitions (keyword substring match). Order matters only for clarity;
    // a domain may land in multiple packs (desired for ergonomics).
    public static readonly IReadOnlyList<PackDefinition> DefaultPacks = new[]
    {
        new PackDefinition("discord",    new[] { "discord", "dis.gd", "discordapp" },
                           ListKind.Hostlist, "domains_all.lst"),
        new PackDefinition("youtube",    new[] { "youtube", "ytimg", "googlevideo", "ggpht", "youtu.be" },
                           ListKind.Hostlist, "domains_all.lst"),
        new PackDefinition("meta",       new[] { "facebook", "instagram", "fbcdn", "whatsapp", "cdninstagram", "messenger.com" },
                           ListKind.Hostlist, "domains_all.lst"),
        new PackDefinition("cloudflare", new[] { "cloudflare", "workers.dev", "cf-ipfs", "1.1.1.1" },
                           ListKind.Hostlist, "domains_all.lst"),
        new PackDefinition("google",     new[] { "google", "gstatic", "googleapis", "googleusercontent" },
                           ListKind.Hostlist, "domains_all.lst"),
        // "general" is special: union of community.lst + residue from domains_all.lst.
        new PackDefinition("general",    Array.Empty<string>(),
                           ListKind.Hostlist, "community.lst"),
    };

    public static readonly IReadOnlyList<PackDefinition> DefaultIpsetPacks = new[]
    {
        new PackDefinition("discord", Array.Empty<string>(), ListKind.Ipset, "discord_ips.lst"),
        new PackDefinition("all",     Array.Empty<string>(), ListKind.Ipset, "ipsum.lst"),
    };

    // *-user.txt files referenced by Flowseal-converted strategies must exist (winws errors on missing files).
    private static readonly string[] UserOverrideFiles =
    {
        "list-general-user.txt",
        "list-exclude-user.txt",
        "ipset-exclude.txt",
        "ipset-exclude-user.txt",
        "list-exclude.txt",  // also expected, empty by default
    };

    private readonly GitHubClient _gh;

    public ListsInstaller(GitHubClient gh)
    {
        _gh = gh;
    }

    public async Task<ListsInstallResult> InstallAsync(ListsOptions opts, CancellationToken ct = default)
    {
        string outDir = Path.Combine(opts.TargetDir, "lists", "bundled");
        Directory.CreateDirectory(outDir);

        Report(opts, "Resolving source commit SHA");
        string? sourceSha = await TryGetLatestCommitShaAsync(ct);

        Report(opts, "Downloading source list files");
        string tempDir = Path.Combine(Path.GetTempPath(), $"zdefree-lists-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var warnings = new List<string>();
        var installed = new List<InstalledPack>();

        try
        {
            // Required source files we may need.
            var sourceFiles = new[]
            {
                "domains_all.lst",
                "community.lst",
                "ooni_domains.lst",
                "ipsum.lst",
                "community_ips.lst",
                "discord_ips.lst",
            };

            var fetched = new Dictionary<string, string[]>();
            for (int i = 0; i < sourceFiles.Length; i++)
            {
                string f = sourceFiles[i];
                int pct = (int)(100.0 * (i + 1) / sourceFiles.Length);
                Report(opts, $"Downloading {f}", pct);

                string url  = $"https://raw.githubusercontent.com/{SourceOwner}/{SourceRepo}/main/{f}";
                string dest = Path.Combine(tempDir, f);
                try
                {
                    await _gh.DownloadToFileAsync(url, dest, expectedSha256: null, progress: null, ct);
                    fetched[f] = await File.ReadAllLinesAsync(dest, ct);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Source fetch failed for {f}: {ex.Message}");
                }
            }

            // Build domain packs.
            var requested = opts.Packs is { Count: > 0 } ? new HashSet<string>(opts.Packs, StringComparer.OrdinalIgnoreCase) : null;
            var residueExcluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pack in DefaultPacks)
            {
                if (requested is not null && !requested.Contains(pack.Name)) continue;
                if (pack.Name == "general") continue; // handled separately as residue

                if (!fetched.TryGetValue(pack.SourceFile, out var lines))
                {
                    warnings.Add($"Pack '{pack.Name}' skipped: source file {pack.SourceFile} not fetched.");
                    continue;
                }

                var matches = lines.Where(l => MatchesAnyKeyword(l, pack.Keywords)).ToList();
                foreach (var l in matches) residueExcluded.Add(l.Trim().ToLowerInvariant());

                var (cleaned, vr) = ListValidator.Validate(matches, ListKind.Hostlist);
                if (vr.Rejected > 0)
                {
                    warnings.Add($"Pack '{pack.Name}': {vr.Rejected} line(s) rejected by validator.");
                }

                string outPath = Path.Combine(outDir, $"list-{pack.Name}.txt");
                if (!opts.ValidateOnly)
                {
                    await WriteLinesAsync(outPath, cleaned, ct);
                }
                installed.Add(new InstalledPack(pack.Name, $"lists/bundled/list-{pack.Name}.txt", cleaned.Count, ListKind.Hostlist));
                Report(opts, $"  list-{pack.Name}.txt ({cleaned.Count} lines)");
            }

            // Special: 'general' pack = community.lst + residue from domains_all.lst.
            if (requested is null || requested.Contains("general"))
            {
                var generalSource = new List<string>();
                if (fetched.TryGetValue("community.lst", out var community)) generalSource.AddRange(community);
                if (fetched.TryGetValue("domains_all.lst", out var allDomains))
                {
                    generalSource.AddRange(allDomains.Where(l => !residueExcluded.Contains(l.Trim().ToLowerInvariant())));
                }
                var (cleaned, vr) = ListValidator.Validate(generalSource, ListKind.Hostlist);
                if (vr.Rejected > 0)
                {
                    warnings.Add($"Pack 'general': {vr.Rejected} line(s) rejected by validator.");
                }

                string outPath = Path.Combine(outDir, "list-general.txt");
                if (!opts.ValidateOnly)
                {
                    await WriteLinesAsync(outPath, cleaned, ct);
                }
                installed.Add(new InstalledPack("general", "lists/bundled/list-general.txt", cleaned.Count, ListKind.Hostlist));
                Report(opts, $"  list-general.txt ({cleaned.Count} lines)");
            }

            // Build ipset packs.
            foreach (var pack in DefaultIpsetPacks)
            {
                if (requested is not null && !requested.Contains(pack.Name)) continue;

                var sources = new List<string>();
                if (pack.Name == "all")
                {
                    foreach (var f in new[] { "ipsum.lst", "community_ips.lst", "discord_ips.lst" })
                    {
                        if (fetched.TryGetValue(f, out var lines)) sources.AddRange(lines);
                    }
                }
                else if (fetched.TryGetValue(pack.SourceFile, out var lines))
                {
                    sources.AddRange(lines);
                }
                else
                {
                    warnings.Add($"Ipset '{pack.Name}' skipped: source file {pack.SourceFile} not fetched.");
                    continue;
                }

                var (cleaned, vr) = ListValidator.Validate(sources, ListKind.Ipset);
                if (vr.Rejected > 0)
                {
                    warnings.Add($"Ipset '{pack.Name}': {vr.Rejected} line(s) rejected by validator.");
                }

                string outPath = Path.Combine(outDir, $"ipset-{pack.Name}.txt");
                if (!opts.ValidateOnly)
                {
                    await WriteLinesAsync(outPath, cleaned, ct);
                }
                installed.Add(new InstalledPack(pack.Name, $"lists/bundled/ipset-{pack.Name}.txt", cleaned.Count, ListKind.Ipset));
                Report(opts, $"  ipset-{pack.Name}.txt ({cleaned.Count} lines)");
            }

            // Always-empty user-override files (winws errors on missing files).
            if (!opts.ValidateOnly)
            {
                foreach (var name in UserOverrideFiles)
                {
                    string path = Path.Combine(outDir, name);
                    if (!File.Exists(path))
                    {
                        await File.WriteAllTextAsync(path, "", ct);
                    }
                }
            }

            // Version file.
            if (!opts.ValidateOnly)
            {
                var versionDoc = new
                {
                    source        = $"{SourceOwner}/{SourceRepo}",
                    source_sha    = sourceSha,
                    generated_at  = DateTimeOffset.UtcNow.ToString("o"),
                    packs         = installed.Select(p => new { name = p.Name, file = p.File, line_count = p.LineCount, kind = p.Kind.ToString().ToLowerInvariant() }).ToList(),
                };
                string versionJson = JsonSerializer.Serialize(versionDoc, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(Path.Combine(outDir, "LISTS_VERSION.json"), versionJson, ct);
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        return new ListsInstallResult(opts.TargetDir, installed, sourceSha, warnings);
    }

    private static bool MatchesAnyKeyword(string line, IReadOnlyList<string> keywords)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0) return false;
        if (trimmed.StartsWith('#')) return false;
        foreach (var kw in keywords)
        {
            if (trimmed.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static async Task WriteLinesAsync(string path, IReadOnlyList<string> lines, CancellationToken ct)
    {
        // Force LF line endings, no BOM, UTF-8.
        var sb = new StringBuilder(lines.Sum(l => l.Length + 1));
        foreach (var l in lines)
        {
            sb.Append(l).Append('\n');
        }
        await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
    }

    private async Task<string?> TryGetLatestCommitShaAsync(CancellationToken ct)
    {
        try
        {
            string url = $"https://api.github.com/repos/{SourceOwner}/{SourceRepo}/commits/main";
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

    private static void Report(ListsOptions opts, string stage, int? pct = null)
    {
        opts.Progress?.Report(new BootstrapProgress("lists", stage, pct));
    }
}
