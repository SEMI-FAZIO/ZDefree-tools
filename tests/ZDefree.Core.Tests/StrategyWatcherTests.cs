using System.Collections.Concurrent;
using ZDefree.Core.Watching;

namespace ZDefree.Core.Tests;

public class StrategyWatcherTests
{
    private static (string root, IDisposable cleanup) MakeTempRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zdefree-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "common"));
        Directory.CreateDirectory(Path.Combine(root, "advanced"));
        return (root, new DeleteOnDispose(root));
    }

    [Fact]
    public void ShouldEmit_filters_out_index_and_schema_files()
    {
        Assert.False(StrategyWatcher.ShouldEmit("/x/INDEX.json"));
        Assert.False(StrategyWatcher.ShouldEmit("/x/index.json"));    // case-insensitive
        Assert.False(StrategyWatcher.ShouldEmit("/x/foo.schema.json"));
        Assert.False(StrategyWatcher.ShouldEmit("/x/foo.txt"));
        Assert.True(StrategyWatcher.ShouldEmit("/x/general.json"));
        Assert.True(StrategyWatcher.ShouldEmit("/x/general-alt.json"));
    }

    [Fact]
    public void Constructor_throws_when_strategies_root_missing()
    {
        string root = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}");
        Assert.Throws<DirectoryNotFoundException>(() => new StrategyWatcher(root));
    }

    [Fact]
    public void Constructor_creates_common_and_advanced_subdirs_if_missing()
    {
        // Root exists but no common/advanced subdirs — watcher should create them.
        string root = Path.Combine(Path.GetTempPath(), $"watch-init-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var watcher = new StrategyWatcher(root);
            Assert.True(Directory.Exists(Path.Combine(root, "common")));
            Assert.True(Directory.Exists(Path.Combine(root, "advanced")));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Watcher_emits_added_event_when_new_strategy_dropped()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        using (var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(50)))
        {
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();

            string path = Path.Combine(root, "common", "new-strat.json");
            await File.WriteAllTextAsync(path, "{\"id\":\"new-strat\"}");

            await WaitForAtLeast(events, count: 1, timeoutMs: 2000);
            Assert.Contains(events, e => e.Kind == StrategyChangeKind.Added && Path.GetFileName(e.FilePath) == "new-strat.json");
        }
    }

    [Fact]
    public async Task Watcher_ignores_INDEX_json_changes()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        using (var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(50)))
        {
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();

            await File.WriteAllTextAsync(Path.Combine(root, "common", "INDEX.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(root, "common", "foo.schema.json"), "{}");

            // Give time for FSW to potentially fire — then assert none came through.
            await Task.Delay(500);
            Assert.Empty(events);
        }
    }

    [Fact]
    public async Task Watcher_debounces_burst_writes_into_single_event()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        using (var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(150)))
        {
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();

            string path = Path.Combine(root, "common", "burst.json");
            // Burst-write: ten quick changes within the debounce window.
            for (int i = 0; i < 10; i++)
            {
                await File.WriteAllTextAsync(path, $"{{\"v\":{i}}}");
                await Task.Delay(10);
            }

            // Wait until debounce window elapses + a margin.
            await Task.Delay(300);
            // Expect 1-2 emitted events for the same file (Added + maybe Modified).
            Assert.InRange(events.Count(e => Path.GetFileName(e.FilePath) == "burst.json"), 1, 3);
        }
    }

    [Fact]
    public async Task Watcher_emits_removed_event_when_file_deleted()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        {
            string path = Path.Combine(root, "common", "doomed.json");
            await File.WriteAllTextAsync(path, "{}");

            using var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(50));
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();

            await Task.Delay(100); // let watcher settle
            File.Delete(path);

            await WaitForAtLeast(events, count: 1, timeoutMs: 2000);
            Assert.Contains(events, e => e.Kind == StrategyChangeKind.Removed && Path.GetFileName(e.FilePath) == "doomed.json");
        }
    }

    [Fact]
    public async Task Watcher_stops_emitting_after_stop_call()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        using (var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(50)))
        {
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();
            watcher.Stop();

            await File.WriteAllTextAsync(Path.Combine(root, "common", "after-stop.json"), "{}");
            await Task.Delay(300);

            Assert.Empty(events);
        }
    }

    [Fact]
    public async Task Watcher_watches_advanced_subdir_too()
    {
        var (root, cleanup) = MakeTempRoot();
        using (cleanup)
        using (var watcher = new StrategyWatcher(root, TimeSpan.FromMilliseconds(50)))
        {
            var events = new ConcurrentBag<StrategyChangeEvent>();
            watcher.Changed += (_, e) => events.Add(e);
            watcher.Start();

            string path = Path.Combine(root, "advanced", "experimental.json");
            await File.WriteAllTextAsync(path, "{}");

            await WaitForAtLeast(events, count: 1, timeoutMs: 2000);
            Assert.Contains(events, e => e.FilePath.Contains("advanced", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static async Task WaitForAtLeast<T>(ConcurrentBag<T> bag, int count, int timeoutMs)
    {
        int waited = 0;
        while (bag.Count < count && waited < timeoutMs)
        {
            await Task.Delay(50);
            waited += 50;
        }
    }

    private sealed class DeleteOnDispose : IDisposable
    {
        private readonly string _path;
        public DeleteOnDispose(string path) { _path = path; }
        public void Dispose()
        {
            try { Directory.Delete(_path, recursive: true); } catch { }
        }
    }
}
