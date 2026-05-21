using System.Net;
using ZDefree.Core.Probing;

namespace ZDefree.Core.Tests;

public class ConnectionProbeTests
{
    [Fact]
    public void ProbeTarget_Parse_accepts_bare_host()
    {
        var t = ProbeTarget.Parse("discord.com");
        Assert.Equal("discord.com", t.Host);
        Assert.Equal(443, t.Port);
    }

    [Fact]
    public void ProbeTarget_Parse_accepts_host_with_port()
    {
        var t = ProbeTarget.Parse("example.com:8443");
        Assert.Equal("example.com", t.Host);
        Assert.Equal(8443, t.Port);
    }

    [Fact]
    public void ProbeTarget_Parse_rejects_empty_and_invalid_port()
    {
        Assert.Throws<ArgumentException>(() => ProbeTarget.Parse(""));
        Assert.Throws<ArgumentException>(() => ProbeTarget.Parse("example.com:abc"));
        Assert.Throws<ArgumentException>(() => ProbeTarget.Parse("example.com:99999"));
    }

    [Fact]
    public void ProbeTarget_ToString_omits_default_port()
    {
        Assert.Equal("discord.com", new ProbeTarget("discord.com").ToString());
        Assert.Equal("example.com:8443", new ProbeTarget("example.com", 8443).ToString());
    }

    [Fact]
    public void ProbeResult_Score_zero_when_all_failed()
    {
        var r = new ProbeResult("x", false, null, false, null, "err");
        Assert.Equal(0, r.Score);
    }

    [Fact]
    public void ProbeResult_Score_higher_for_lower_latency()
    {
        var fast = new ProbeResult("x", true, 50, false, null, null);
        var slow = new ProbeResult("x", true, 1000, false, null, null);
        Assert.True(fast.Score > slow.Score);
    }

    [Fact]
    public void ProbeResult_Score_caps_at_max_with_ping_success()
    {
        var perfect = new ProbeResult("x", true, 1, true, 1, null);
        Assert.True(perfect.Score >= 79 && perfect.Score <= 80);
    }

    [Fact]
    public async Task RunAsync_returns_success_with_mocked_http_client()
    {
        using var probe = new ConnectionProbe(MockHttp(HttpStatusCode.OK, delayMs: 20));
        var results = await probe.RunAsync(new ProbeOptions
        {
            Targets   = new[] { new ProbeTarget("example.com") },
            TimeoutMs = 5000,
            SkipPing  = true,
        });

        Assert.Single(results);
        Assert.True(results[0].HttpsOk);
        Assert.NotNull(results[0].HttpsMs);
        Assert.Null(results[0].Error);
    }

    [Fact]
    public async Task RunAsync_records_failure_when_handler_throws()
    {
        using var probe = new ConnectionProbe(MockHttpThrow(new HttpRequestException("connection refused")));
        var results = await probe.RunAsync(new ProbeOptions
        {
            Targets   = new[] { new ProbeTarget("example.com") },
            TimeoutMs = 5000,
            SkipPing  = true,
        });

        Assert.False(results[0].HttpsOk);
        Assert.NotNull(results[0].Error);
        Assert.Contains("HttpRequestException", results[0].Error);
    }

    [Fact]
    public async Task RunAsync_records_timeout_message_when_handler_hangs()
    {
        using var probe = new ConnectionProbe(MockHttp(HttpStatusCode.OK, delayMs: 2000));
        var results = await probe.RunAsync(new ProbeOptions
        {
            Targets   = new[] { new ProbeTarget("example.com") },
            TimeoutMs = 100,
            SkipPing  = true,
        });

        Assert.False(results[0].HttpsOk);
        Assert.NotNull(results[0].Error);
        Assert.Contains("timeout", results[0].Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_probes_all_targets_in_parallel()
    {
        using var probe = new ConnectionProbe(MockHttp(HttpStatusCode.OK, delayMs: 100));
        var targets = new[]
        {
            new ProbeTarget("a.example.com"),
            new ProbeTarget("b.example.com"),
            new ProbeTarget("c.example.com"),
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var results = await probe.RunAsync(new ProbeOptions
        {
            Targets = targets, TimeoutMs = 5000, SkipPing = true,
        });
        sw.Stop();

        Assert.Equal(3, results.Count);
        // Parallel: ~100ms total, not 300ms. Allow generous headroom for CI variance.
        Assert.True(sw.ElapsedMilliseconds < 500, $"Expected parallel ~100ms, got {sw.ElapsedMilliseconds}ms");
    }

    // ---- Helpers ----

    private static HttpClient MockHttp(HttpStatusCode status, int delayMs = 0)
    {
        return new HttpClient(new StubHandler(async ct =>
        {
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            return new HttpResponseMessage(status);
        }));
    }

    private static HttpClient MockHttpThrow(Exception ex)
    {
        return new HttpClient(new StubHandler(_ => throw ex));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<CancellationToken, Task<HttpResponseMessage>> _respond;
        public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond) { _respond = respond; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => _respond(ct);
    }
}
