using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;

namespace ZDefree.Core.Probing;

public sealed record ProbeTarget(string Host, int Port = 443)
{
    /// <summary>
    /// Parse "host" or "host:port" form. Default port is 443.
    /// </summary>
    public static ProbeTarget Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            throw new ArgumentException("Target spec cannot be empty", nameof(spec));
        }
        int colon = spec.LastIndexOf(':');
        if (colon < 0)
        {
            return new ProbeTarget(spec.Trim());
        }
        string host = spec.Substring(0, colon).Trim();
        if (!int.TryParse(spec.AsSpan(colon + 1), out int port) || port < 1 || port > 65535)
        {
            throw new ArgumentException($"Invalid port in target spec: '{spec}'", nameof(spec));
        }
        return new ProbeTarget(host, port);
    }

    public override string ToString() => Port == 443 ? Host : $"{Host}:{Port}";
}

public sealed record ProbeResult(
    string Target,
    bool HttpsOk,
    double? HttpsMs,
    bool PingOk,
    double? PingMs,
    string? Error)
{
    /// <summary>
    /// A heuristic score in [0, 100]. Higher is better. Used by StrategyPicker.
    /// Logic: HTTPS success = 60 base; subtract latency penalty (1 per 50ms).
    ///        Ping success = 20 base; subtract latency penalty (1 per 10ms).
    /// </summary>
    public double Score
    {
        get
        {
            double s = 0;
            if (HttpsOk)
            {
                s += 60;
                if (HttpsMs is double hms) s -= Math.Min(40, hms / 50.0);
            }
            if (PingOk)
            {
                s += 20;
                if (PingMs is double pms) s -= Math.Min(15, pms / 10.0);
            }
            return Math.Max(0, s);
        }
    }
}

public sealed record ProbeOptions
{
    public required IReadOnlyList<ProbeTarget> Targets { get; init; }
    public int TimeoutMs { get; init; } = 3000;
    public bool SkipPing { get; init; } = false;
}

public interface IConnectionProbe
{
    Task<IReadOnlyList<ProbeResult>> RunAsync(ProbeOptions options, CancellationToken ct = default);
}

public sealed class ConnectionProbe : IConnectionProbe, IDisposable
{
    public static readonly IReadOnlyList<ProbeTarget> DefaultTargets = new[]
    {
        new ProbeTarget("discord.com"),
        new ProbeTarget("www.youtube.com"),
        new ProbeTarget("www.google.com"),
        new ProbeTarget("github.com"),
    };

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public ConnectionProbe(HttpClient? http = null)
    {
        if (http is null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CheckCertificateRevocationList = false,
            };
            _http = new HttpClient(handler);
            _ownsClient = true;
        }
        else
        {
            _http = http;
            _ownsClient = false;
        }

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZDefree-Probe/0.1");
        }
    }

    public async Task<IReadOnlyList<ProbeResult>> RunAsync(ProbeOptions options, CancellationToken ct = default)
    {
        var tasks = options.Targets
            .Select(t => ProbeOneAsync(t, options.TimeoutMs, options.SkipPing, ct))
            .ToList();
        return await Task.WhenAll(tasks);
    }

    private async Task<ProbeResult> ProbeOneAsync(ProbeTarget target, int timeoutMs, bool skipPing, CancellationToken ct)
    {
        var (httpsOk, httpsMs, httpsErr) = await ProbeHttpsAsync(target, timeoutMs, ct);
        var (pingOk, pingMs, pingErr) = skipPing
            ? (false, (double?)null, (string?)null)
            : await ProbePingAsync(target.Host, timeoutMs, ct);

        string? error = httpsErr ?? pingErr;
        return new ProbeResult(target.ToString(), httpsOk, httpsMs, pingOk, pingMs, error);
    }

    private async Task<(bool ok, double? ms, string? err)> ProbeHttpsAsync(ProbeTarget target, int timeoutMs, CancellationToken ct)
    {
        var url = $"https://{target.Host}:{target.Port}/";
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            sw.Stop();
            // Any HTTP response (incl. 4xx/5xx) means TCP+TLS succeeded — that's what we measure.
            return (true, sw.Elapsed.TotalMilliseconds, null);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return (false, null, $"HTTPS timeout after {timeoutMs}ms");
        }
        catch (Exception ex)
        {
            return (false, null, $"HTTPS error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<(bool ok, double? ms, string? err)> ProbePingAsync(string host, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            // .NET's Ping doesn't take a CancellationToken, but it does honor a timeout (ms).
            var reply = await ping.SendPingAsync(host, timeoutMs);
            if (reply.Status == IPStatus.Success)
            {
                return (true, reply.RoundtripTime, null);
            }
            return (false, null, $"Ping {reply.Status}");
        }
        catch (Exception ex)
        {
            return (false, null, $"Ping error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
