using ZDefree.Core.Models;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Probing;

public sealed record PickerCandidate(
    string Id,
    string Name,
    string? Category,
    string File,
    bool IspMatched,
    string? MatchedIspTag);

public sealed record PickerResult(
    IspInfo? DetectedIsp,
    IReadOnlyList<PickerCandidate> Candidates);

public sealed record PickerOptions
{
    public required string StrategiesDir { get; init; }

    /// <summary>
    /// "auto" — detect via IIspDetector; "none" — skip ISP matching;
    /// any other string — use as the compat tag verbatim (e.g. "AS8402").
    /// </summary>
    public string IspMode { get; init; } = "none";

    /// <summary>
    /// Dry-run only ranks candidates without running probe/winws.
    /// Live mode is currently informational; full orchestration is deferred
    /// to a future sprint that ships winws process management.
    /// </summary>
    public bool DryRun { get; init; } = true;
}

public sealed class StrategyPicker
{
    private readonly IIspDetector? _ispDetector;

    public StrategyPicker(IIspDetector? ispDetector = null)
    {
        _ispDetector = ispDetector;
    }

    public async Task<PickerResult> RunAsync(PickerOptions opts, CancellationToken ct = default)
    {
        string indexPath = Path.Combine(opts.StrategiesDir, "INDEX.json");
        if (!File.Exists(indexPath))
        {
            throw new FileNotFoundException($"INDEX.json not found. Run `zdefree index` first.", indexPath);
        }

        var index = StrategyIndexBuilder.Deserialize(await File.ReadAllTextAsync(indexPath, ct));

        IspInfo? detected = null;
        string? matchTag = null;
        if (opts.IspMode == "auto" && _ispDetector is not null)
        {
            try
            {
                detected = await _ispDetector.DetectAsync(ct);
                matchTag = detected.CompatTag ?? detected.Asn;
            }
            catch
            {
                // ISP detection is best-effort; on failure we fall back to no filter.
            }
        }
        else if (opts.IspMode != "auto" && opts.IspMode != "none" && !string.IsNullOrWhiteSpace(opts.IspMode))
        {
            matchTag = opts.IspMode;
        }

        var candidates = new List<PickerCandidate>();
        foreach (var entry in index.Strategies)
        {
            string fullPath = Path.Combine(opts.StrategiesDir, entry.File);
            Strategy? full = null;
            try
            {
                full = StrategyLoader.LoadFromFile(fullPath);
            }
            catch
            {
                // If individual strategy fails to load, skip it but keep going.
                continue;
            }

            bool matched = false;
            string? whichTag = null;
            if (!string.IsNullOrEmpty(matchTag) && full.Compat?.TestedIsps is { Count: > 0 } tested)
            {
                foreach (var t in tested)
                {
                    if (t.StartsWith(matchTag, StringComparison.OrdinalIgnoreCase) ||
                        matchTag.StartsWith(t,  StringComparison.OrdinalIgnoreCase))
                    {
                        matched = true;
                        whichTag = t;
                        break;
                    }
                }
            }

            candidates.Add(new PickerCandidate(
                Id: entry.Id,
                Name: entry.Name,
                Category: entry.Category,
                File: entry.File,
                IspMatched: matched,
                MatchedIspTag: whichTag));
        }

        // Order: ISP-matched first, then by category, then by id.
        candidates.Sort((a, b) =>
        {
            if (a.IspMatched != b.IspMatched) return a.IspMatched ? -1 : 1;
            int c = string.Compare(a.Category ?? "zz", b.Category ?? "zz", StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        return new PickerResult(detected, candidates);
    }
}
