using ZDefree.Core.Probing;

namespace ZDefree.Core.Tests;

public class StrategyPickerLiveTests
{
    private static string PickerFixtureRoot() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "picker");

    private static LivePickerSettings DefaultLive() => new()
    {
        WinwsExePath        = @"C:\fake\winws.exe",
        BinDir              = "bin/",
        ListsDir            = "lists/",
        StabilizationWait   = TimeSpan.Zero,
        PerStrategyTimeout  = TimeSpan.FromSeconds(5),
    };

    [Fact]
    public async Task RunLiveAsync_returns_scored_candidates_when_all_strategies_start()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(score: 60);
        int startCalls = 0;

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () => new FakeWinwsProcess(succeedStart: true, recordStart: () => Interlocked.Increment(ref startCalls)),
            DefaultLive());

        Assert.Equal(3, result.Ranked.Count);
        Assert.Equal(3, startCalls);
        Assert.All(result.Ranked, c => Assert.True(c.Score > 0));
        Assert.All(result.Ranked, c => Assert.Null(c.Error));
    }

    [Fact]
    public async Task RunLiveAsync_sorts_by_score_descending()
    {
        var picker = new StrategyPicker();
        // Variable scoring per call: first probe scores high, others low.
        int call = 0;
        var probe = new FakeProbe(scoreFactory: () =>
        {
            int n = Interlocked.Increment(ref call);
            return n == 1 ? 80 : 20;
        });

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () => new FakeWinwsProcess(succeedStart: true),
            DefaultLive());

        // Ranked DESC by score: 80 first, then 20s.
        Assert.True(result.Ranked[0].Score >= result.Ranked[1].Score);
        Assert.True(result.Ranked[1].Score >= result.Ranked[2].Score);
    }

    [Fact]
    public async Task RunLiveAsync_records_zero_score_and_error_when_winws_fails_to_start()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(score: 60);

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () => new FakeWinwsProcess(succeedStart: false, simulatedLastError: "WinDivert busy"),
            DefaultLive());

        Assert.All(result.Ranked, c => Assert.Equal(0, c.Score));
        Assert.All(result.Ranked, c => Assert.Equal("WinDivert busy", c.Error));
    }

    [Fact]
    public async Task RunLiveAsync_stops_each_process_even_when_probe_throws()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(throwInstead: new InvalidOperationException("network unplugged"));
        var processes = new List<FakeWinwsProcess>();

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () =>
            {
                var p = new FakeWinwsProcess(succeedStart: true);
                processes.Add(p);
                return p;
            },
            DefaultLive());

        Assert.Equal(3, processes.Count);
        Assert.All(processes, p => Assert.True(p.StopCalled));
        Assert.All(result.Ranked, c =>
        {
            Assert.Equal(0, c.Score);
            Assert.Contains("probe failed", c.Error);
        });
    }

    [Fact]
    public async Task RunLiveAsync_disposes_each_process_after_stop()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(score: 50);
        var processes = new List<FakeWinwsProcess>();

        await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () =>
            {
                var p = new FakeWinwsProcess(succeedStart: true);
                processes.Add(p);
                return p;
            },
            DefaultLive());

        Assert.All(processes, p => Assert.True(p.DisposeCalled));
    }

    [Fact]
    public async Task RunLiveAsync_records_per_strategy_timeout_during_stabilization()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(score: 60);

        var liveSettings = DefaultLive() with
        {
            StabilizationWait  = TimeSpan.FromSeconds(10),
            PerStrategyTimeout = TimeSpan.FromMilliseconds(100),
        };

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () => new FakeWinwsProcess(succeedStart: true),
            liveSettings);

        Assert.All(result.Ranked, c =>
        {
            Assert.Equal(0, c.Score);
            Assert.Contains("timeout during stabilization", c.Error);
        });
    }

    [Fact]
    public async Task RunLiveAsync_compiles_args_and_passes_them_to_process()
    {
        var picker = new StrategyPicker();
        var probe  = new FakeProbe(score: 60);
        string? capturedArgs = null;

        var result = await picker.RunLiveAsync(
            new PickerOptions { StrategiesDir = PickerFixtureRoot() },
            probe,
            () => new FakeWinwsProcess(succeedStart: true,
                captureArgs: a => capturedArgs ??= a),
            DefaultLive());

        Assert.NotNull(capturedArgs);
        Assert.Contains("--wf-tcp",     capturedArgs);
        Assert.Contains("--dpi-desync", capturedArgs);
    }

    // ---- Helpers ----

    private sealed class FakeWinwsProcess : IWinwsProcess
    {
        private readonly bool _succeedStart;
        private readonly string? _simulatedLastError;
        private readonly Action? _recordStart;
        private readonly Action<string>? _captureArgs;

        public bool StopCalled    { get; private set; }
        public bool DisposeCalled { get; private set; }
        public bool IsRunning     { get; private set; }
        public string? LastError  => _simulatedLastError;

        public FakeWinwsProcess(
            bool succeedStart,
            string? simulatedLastError = null,
            Action? recordStart = null,
            Action<string>? captureArgs = null)
        {
            _succeedStart       = succeedStart;
            _simulatedLastError = simulatedLastError;
            _recordStart        = recordStart;
            _captureArgs        = captureArgs;
        }

        public Task<bool> StartAsync(string exePath, string args, CancellationToken ct)
        {
            _recordStart?.Invoke();
            _captureArgs?.Invoke(args);
            IsRunning = _succeedStart;
            return Task.FromResult(_succeedStart);
        }

        public Task StopAsync(CancellationToken ct)
        {
            StopCalled = true;
            IsRunning  = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeProbe : IConnectionProbe
    {
        private readonly double _fixed;
        private readonly Func<double>? _factory;
        private readonly Exception? _throw;

        public FakeProbe(double score)                         { _fixed = score; }
        public FakeProbe(Func<double> scoreFactory)            { _factory = scoreFactory; }
        public FakeProbe(Exception throwInstead)               { _throw = throwInstead; }

        public Task<IReadOnlyList<ProbeResult>> RunAsync(ProbeOptions options, CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;
            double s = _factory?.Invoke() ?? _fixed;
            // Synthesize a ProbeResult that produces the requested score.
            // Score formula: HTTPS ok (60) - latency_penalty + ping ok (20) - ping_penalty.
            // For simplicity, set HttpsOk=true with latency chosen to land near s.
            var results = options.Targets.Select(t =>
            {
                double httpsMs = s >= 60 ? Math.Max(0, (60 - (s - 0))) * 50 : 1500; // approximate
                return new ProbeResult(t.ToString(), s > 0, httpsMs, false, null, null);
            }).ToList();
            return Task.FromResult<IReadOnlyList<ProbeResult>>(results);
        }
    }
}
