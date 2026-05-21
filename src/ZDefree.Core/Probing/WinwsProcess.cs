using System.Diagnostics;

namespace ZDefree.Core.Probing;

/// <summary>
/// Real <see cref="IWinwsProcess"/> implementation backed by
/// <see cref="System.Diagnostics.Process"/>. Runs winws.exe (or nfqws on
/// Linux) with the given args, captures the most recent stderr line, and
/// kills the process tree on stop/dispose.
///
/// winws.exe requires administrator privileges (it loads the WinDivert
/// kernel driver). Callers must ensure the host process is elevated; this
/// class doesn't attempt UAC.
/// </summary>
public sealed class WinwsProcess : IWinwsProcess
{
    private Process? _process;
    private string? _lastError;
    private readonly object _gate = new();
    private TimeSpan _startupGrace = TimeSpan.FromMilliseconds(500);

    public WinwsProcess(TimeSpan? startupGrace = null)
    {
        if (startupGrace.HasValue) _startupGrace = startupGrace.Value;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { } p && !SafeHasExited(p);
            }
        }
    }

    public string? LastError
    {
        get { lock (_gate) return _lastError; }
    }

    public async Task<bool> StartAsync(string exePath, string args, CancellationToken ct)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Process already running. Stop it before starting a new run.");
        }

        if (string.IsNullOrEmpty(exePath))
        {
            throw new ArgumentException("Exe path is required", nameof(exePath));
        }

        var psi = new ProcessStartInfo
        {
            FileName               = exePath,
            Arguments              = args,
            UseShellExecute        = false,
            RedirectStandardError  = true,
            RedirectStandardOutput = true,
            CreateNoWindow         = true,
        };

        var p = new Process { StartInfo = psi, EnableRaisingEvents = true };

        p.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (_gate) _lastError = e.Data;
            }
        };

        if (!p.Start())
        {
            return false;
        }

        p.BeginErrorReadLine();
        p.BeginOutputReadLine();

        lock (_gate) _process = p;

        // Give it time to either bind WinDivert successfully or fail and exit.
        try
        {
            await Task.Delay(_startupGrace, ct);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(CancellationToken.None);
            throw;
        }

        return !SafeHasExited(p);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Process? p;
        lock (_gate) { p = _process; _process = null; }
        if (p is null) return;

        try
        {
            if (!SafeHasExited(p))
            {
                // Kill the whole tree — winws spawns no children today, but be defensive.
                try { p.Kill(entireProcessTree: true); } catch { /* already dead */ }
                await p.WaitForExitAsync(ct);
            }
        }
        finally
        {
            try { p.Dispose(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private static bool SafeHasExited(Process p)
    {
        try { return p.HasExited; } catch { return true; }
    }
}
