using ZDefree.Core.Probing;

namespace ZDefree.Core.Tests;

public class StrategyPickerTests
{
    private static string PickerFixtureRoot() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "picker");

    [Fact]
    public async Task RunAsync_returns_all_candidates_with_no_isp_filter()
    {
        var picker = new StrategyPicker();
        var result = await picker.RunAsync(new PickerOptions
        {
            StrategiesDir = PickerFixtureRoot(),
            IspMode       = "none",
        });

        Assert.Null(result.DetectedIsp);
        Assert.Equal(3, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.False(c.IspMatched));
    }

    [Fact]
    public async Task RunAsync_marks_isp_matched_candidate_with_explicit_tag()
    {
        var picker = new StrategyPicker();
        var result = await picker.RunAsync(new PickerOptions
        {
            StrategiesDir = PickerFixtureRoot(),
            IspMode       = "AS8402",
        });

        var matched = result.Candidates.Single(c => c.IspMatched);
        Assert.Equal("with-isp-match", matched.Id);
        Assert.Equal("AS8402-Vimpelcom", matched.MatchedIspTag);
    }

    [Fact]
    public async Task RunAsync_ranks_isp_matched_candidates_first()
    {
        var picker = new StrategyPicker();
        var result = await picker.RunAsync(new PickerOptions
        {
            StrategiesDir = PickerFixtureRoot(),
            IspMode       = "AS8402",
        });

        Assert.Equal("with-isp-match", result.Candidates[0].Id);
        // After the matched one, alphabetical by category then id.
        Assert.False(result.Candidates[1].IspMatched);
    }

    [Fact]
    public async Task RunAsync_uses_detector_for_auto_mode()
    {
        var fakeDetector = new FakeIspDetector(new IspInfo("1.2.3.4", "RU", "AS12389", "Rostelecom", "raw"));
        var picker = new StrategyPicker(fakeDetector);
        var result = await picker.RunAsync(new PickerOptions
        {
            StrategiesDir = PickerFixtureRoot(),
            IspMode       = "auto",
        });

        Assert.NotNull(result.DetectedIsp);
        Assert.Equal("AS12389", result.DetectedIsp!.Asn);
        var matched = result.Candidates.Single(c => c.IspMatched);
        Assert.Equal("with-isp-match", matched.Id);
        Assert.Equal("AS12389-Rostelecom", matched.MatchedIspTag);
    }

    [Fact]
    public async Task RunAsync_handles_detector_failure_gracefully()
    {
        var failing = new FakeIspDetector(throwInstead: new InvalidOperationException("network down"));
        var picker = new StrategyPicker(failing);
        var result = await picker.RunAsync(new PickerOptions
        {
            StrategiesDir = PickerFixtureRoot(),
            IspMode       = "auto",
        });

        Assert.Null(result.DetectedIsp);
        Assert.Equal(3, result.Candidates.Count);
        Assert.All(result.Candidates, c => Assert.False(c.IspMatched));
    }

    [Fact]
    public async Task RunAsync_throws_when_INDEX_missing()
    {
        var picker = new StrategyPicker();
        await Assert.ThrowsAsync<FileNotFoundException>(() => picker.RunAsync(new PickerOptions
        {
            StrategiesDir = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}"),
        }));
    }

    [Fact]
    public async Task RunAsync_skips_strategy_files_that_fail_to_load_without_aborting()
    {
        // Picker fixture has only valid strategies; this test verifies the behavior
        // by pointing at a temp directory with one broken file alongside INDEX.json
        // that references it.
        string tempRoot = Path.Combine(Path.GetTempPath(), $"picker-broken-{Guid.NewGuid():N}");
        string common  = Path.Combine(tempRoot, "common");
        Directory.CreateDirectory(common);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(common, "broken.json"), "{ this is not json");
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "INDEX.json"), """
                {
                  "schema_version": 1,
                  "generated_at": "2026-05-21T00:00:00Z",
                  "generator": "test",
                  "strategies": [
                    { "id": "broken", "name": "Broken", "file": "common/broken.json" }
                  ]
                }
                """);

            var picker = new StrategyPicker();
            var result = await picker.RunAsync(new PickerOptions { StrategiesDir = tempRoot });

            Assert.Empty(result.Candidates);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private sealed class FakeIspDetector : IIspDetector
    {
        private readonly IspInfo? _info;
        private readonly Exception? _throw;

        public FakeIspDetector(IspInfo info)      { _info = info; }
        public FakeIspDetector(Exception throwInstead) { _throw = throwInstead; }

        public Task<IspInfo> DetectAsync(CancellationToken ct = default)
        {
            if (_throw is not null) throw _throw;
            return Task.FromResult(_info!);
        }
    }
}
