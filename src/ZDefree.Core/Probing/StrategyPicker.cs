using ZDefree.Core.Compilation;
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
    /// </summary>
    public bool DryRun { get; init; } = true;
}

/// <summary>
/// Configuration for <see cref="StrategyPicker.RunLiveAsync"/>. Required
/// because live mode needs paths to the winws.exe binary, lists, and bin
/// dir to compile per-strategy command lines.
/// </summary>
public sealed record LivePickerSettings
{
    public required string WinwsExePath { get; init; }
    public required string BinDir       { get; init; }
    public required string ListsDir     { get; init; }

    public IReadOnlyList<ProbeTarget>? Targets { get; init; }
    public TimeSpan StabilizationWait { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan PerStrategyTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public string? GameTcpPorts { get; init; }
    public string? GameUdpPorts { get; init; }
}

/// <summary>
/// One row of <see cref="LivePickerResult"/>. Score is the mean of probe scores
/// (0 if the process failed to start or all probes failed).
/// </summary>
public sealed record LiveCandidate(
    PickerCandidate Base,
    double Score,
    IReadOnlyList<ProbeResult> ProbeResults,
    string? Error);

public sealed record LivePickerResult(
    IspInfo? DetectedIsp,
    IReadOnlyList<LiveCandidate> Ranked);

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

    /// <summary>
    /// Runs each ranked candidate live: spawn winws.exe with its compiled
    /// args, wait for stabilization, probe, score, kill. Returns candidates
    /// sorted by score DESC.
    /// </summary>
    /// <param name="opts">Same options as <see cref="RunAsync"/>.</param>
    /// <param name="probe">Connection probe (real or mocked).</param>
    /// <param name="processFactory">Creates a fresh <see cref="IWinwsProcess"/> per iteration.</param>
    /// <param name="live">Live-mode settings (winws path, dirs, timeouts).</param>
    public async Task<LivePickerResult> RunLiveAsync(
        PickerOptions opts,
        IConnectionProbe probe,
        Func<IWinwsProcess> processFactory,
        LivePickerSettings live,
        CancellationToken ct = default)
    {
        var dry = await RunAsync(opts, ct);

        var liveResults = new List<LiveCandidate>(dry.Candidates.Count);
        var compileOpts = new CompileOptions
        {
            BinDir       = live.BinDir,
            ListsDir     = live.ListsDir,
            GameTcpPorts = live.GameTcpPorts,
            GameUdpPorts = live.GameUdpPorts,
        };
        var compiler = new WinwsCompiler(compileOpts);
        var targets  = live.Targets ?? ConnectionProbe.DefaultTargets;

        foreach (var candidate in dry.Candidates)
        {
            ct.ThrowIfCancellationRequested();
            var item = await EvaluateOneAsync(candidate, opts.StrategiesDir, compiler, probe, processFactory, live, targets, ct);
            liveResults.Add(item);
        }

        liveResults.Sort((a, b) => b.Score.CompareTo(a.Score));
        return new LivePickerResult(dry.DetectedIsp, liveResults);
    }

    private static async Task<LiveCandidate> EvaluateOneAsync(
        PickerCandidate candidate,
        string strategiesDir,
        WinwsCompiler compiler,
        IConnectionProbe probe,
        Func<IWinwsProcess> processFactory,
        LivePickerSettings live,
        IReadOnlyList<ProbeTarget> targets,
        CancellationToken outerCt)
    {
        using var perStratCts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        perStratCts.CancelAfter(live.PerStrategyTimeout);
        var ct = perStratCts.Token;

        string fullPath = Path.Combine(strategiesDir, candidate.File);
        Strategy strategy;
        try
        {
            strategy = StrategyLoader.LoadFromFile(fullPath);
        }
        catch (Exception ex)
        {
            return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(), $"load failed: {ex.Message}");
        }

        string args;
        try
        {
            args = compiler.Compile(strategy);
        }
        catch (Exception ex)
        {
            return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(), $"compile failed: {ex.Message}");
        }

        var proc = processFactory();
        try
        {
            bool started;
            try
            {
                started = await proc.StartAsync(live.WinwsExePath, args, ct);
            }
            catch (Exception ex)
            {
                return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(), $"start failed: {ex.Message}");
            }

            if (!started)
            {
                return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(),
                    proc.LastError ?? "winws exited immediately");
            }

            try
            {
                await Task.Delay(live.StabilizationWait, ct);
            }
            catch (OperationCanceledException)
            {
                return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(), "per-strategy timeout during stabilization");
            }

            IReadOnlyList<ProbeResult> probeResults;
            try
            {
                probeResults = await probe.RunAsync(new ProbeOptions
                {
                    Targets   = targets,
                    SkipPing  = false,
                    TimeoutMs = 3000,
                }, ct);
            }
            catch (Exception ex)
            {
                return new LiveCandidate(candidate, 0, Array.Empty<ProbeResult>(), $"probe failed: {ex.Message}");
            }

            double score = probeResults.Count == 0
                ? 0
                : probeResults.Average(r => r.Score);

            return new LiveCandidate(candidate, score, probeResults, null);
        }
        finally
        {
            try { await proc.StopAsync(CancellationToken.None); } catch { }
            try { await proc.DisposeAsync(); } catch { }
        }
    }
}
