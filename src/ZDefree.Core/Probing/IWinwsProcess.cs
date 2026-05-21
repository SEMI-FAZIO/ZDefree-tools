namespace ZDefree.Core.Probing;

/// <summary>
/// One-shot abstraction over a winws.exe (or nfqws) process lifecycle.
/// Each instance wraps a single OS process; create a fresh one per strategy
/// iteration in <see cref="StrategyPicker.RunLiveAsync"/>.
///
/// Implementations must be safe to Dispose mid-flight: that should kill the
/// child process even if it's still running.
/// </summary>
public interface IWinwsProcess : IAsyncDisposable
{
    /// <summary>
    /// Spawn the executable with the given command-line args.
    /// Returns true if the process is alive after a brief startup grace window.
    /// </summary>
    Task<bool> StartAsync(string exePath, string args, CancellationToken ct);

    /// <summary>
    /// Gracefully terminate the running process. Idempotent: no-op if not running.
    /// </summary>
    Task StopAsync(CancellationToken ct);

    bool IsRunning { get; }

    /// <summary>Last stderr line captured during the run, if any. For diagnostics.</summary>
    string? LastError { get; }
}
