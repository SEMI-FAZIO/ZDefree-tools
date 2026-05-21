using System.Text;
using ZDefree.Core.Models;

namespace ZDefree.Core.Compilation;

public sealed class CompileOptions
{
    public string ListsDir { get; init; } = "lists/";
    public string BinDir { get; init; } = "bin/";

    public string? GameTcpPorts { get; init; }
    public string? GameUdpPorts { get; init; }

    public bool QuoteAllArgs { get; init; } = true;
    public string NewSeparator { get; init; } = "--new";
}

public sealed class WinwsCompiler
{
    private readonly CompileOptions _opt;

    public WinwsCompiler(CompileOptions? options = null)
    {
        _opt = options ?? new CompileOptions();
    }

    public string Compile(Strategy strategy)
    {
        if (strategy.Filters.Count == 0)
        {
            throw new InvalidOperationException("Strategy has no filter groups");
        }

        var args = new List<string>();

        var topFilter = strategy.Filters[0];
        EmitWfFlags(args, topFilter.Wf);

        bool firstRule = true;
        foreach (var rule in topFilter.Rules)
        {
            if (!firstRule)
            {
                args.Add(_opt.NewSeparator);
            }
            EmitRule(args, rule);
            firstRule = false;
        }

        for (int i = 1; i < strategy.Filters.Count; i++)
        {
            var extraGroup = strategy.Filters[i];
            args.Add(_opt.NewSeparator);
            EmitWfFlags(args, extraGroup.Wf);
            bool firstInGroup = true;
            foreach (var rule in extraGroup.Rules)
            {
                if (!firstInGroup)
                {
                    args.Add(_opt.NewSeparator);
                }
                EmitRule(args, rule);
                firstInGroup = false;
            }
        }

        return string.Join(' ', args);
    }

    private void EmitWfFlags(List<string> args, WfBlock wf)
    {
        if (!string.IsNullOrEmpty(wf.Tcp))
        {
            args.Add($"--wf-tcp={Subst(wf.Tcp)}");
        }
        if (!string.IsNullOrEmpty(wf.Udp))
        {
            args.Add($"--wf-udp={Subst(wf.Udp)}");
        }
        if (!string.IsNullOrEmpty(wf.Iface))
        {
            args.Add($"--wf-iface={wf.Iface}");
        }
        if (!string.IsNullOrEmpty(wf.Raw))
        {
            args.Add(wf.Raw);
        }
    }

    private void EmitRule(List<string> args, Rule rule)
    {
        if (rule.Match is { } m)
        {
            if (!string.IsNullOrEmpty(m.Tcp)) args.Add($"--filter-tcp={Subst(m.Tcp)}");
            if (!string.IsNullOrEmpty(m.Udp)) args.Add($"--filter-udp={Subst(m.Udp)}");
            if (!string.IsNullOrEmpty(m.L3))  args.Add($"--filter-l3={m.L3}");
            if (m.L7 is { Count: > 0 })       args.Add($"--filter-l7={string.Join(',', m.L7)}");
            if (!string.IsNullOrEmpty(m.SsidFilter)) args.Add($"--ssid-filter={m.SsidFilter}");
        }

        if (rule.Hostlist is { Count: > 0 })
            foreach (var l in rule.Hostlist)
                args.Add($"--hostlist={Quote(ResolveList(l, "list-"))}");

        if (rule.HostlistExclude is { Count: > 0 })
            foreach (var l in rule.HostlistExclude)
                args.Add($"--hostlist-exclude={Quote(ResolveList(l, "list-"))}");

        if (rule.HostlistDomains is { Count: > 0 })
            args.Add($"--hostlist-domains={string.Join(',', rule.HostlistDomains)}");

        if (rule.Ipset is { Count: > 0 })
            foreach (var l in rule.Ipset)
                args.Add($"--ipset={Quote(ResolveList(l, "ipset-"))}");

        if (rule.IpsetExclude is { Count: > 0 })
            foreach (var l in rule.IpsetExclude)
                args.Add($"--ipset-exclude={Quote(ResolveList(l, "ipset-"))}");

        if (!string.IsNullOrEmpty(rule.IpId))
            args.Add($"--ip-id={rule.IpId}");

        if (rule.Desync is not null)
        {
            EmitDesync(args, rule.Desync);
        }

        if (rule.Advanced is not null)
        {
            EmitAdvanced(args, rule.Advanced);
        }
    }

    private void EmitDesync(List<string> args, DesyncCommon d)
    {
        if (!string.IsNullOrEmpty(d.Mode))
            args.Add($"--dpi-desync={d.Mode}");

        if (d.Ttl is int ttl)
            args.Add($"--dpi-desync-ttl={ttl}");

        if (d.Ttl6 is int ttl6)
            args.Add($"--dpi-desync-ttl6={ttl6}");

        if (d.Fooling is { Count: > 0 })
            args.Add($"--dpi-desync-fooling={string.Join(',', d.Fooling)}");

        if (d.Repeats is int r)
            args.Add($"--dpi-desync-repeats={r}");

        if (!string.IsNullOrEmpty(d.SplitPos))
            args.Add($"--dpi-desync-split-pos={d.SplitPos}");

        if (d.SplitSeqovl is int sso)
            args.Add($"--dpi-desync-split-seqovl={sso}");

        if (!string.IsNullOrEmpty(d.SplitSeqovlPattern))
            args.Add($"--dpi-desync-split-seqovl-pattern={Quote(ResolveBin(d.SplitSeqovlPattern))}");

        if (!string.IsNullOrEmpty(d.FakeTls))
            args.Add($"--dpi-desync-fake-tls={Quote(ResolveBin(d.FakeTls))}");

        if (!string.IsNullOrEmpty(d.FakeQuic))
            args.Add($"--dpi-desync-fake-quic={Quote(ResolveBin(d.FakeQuic))}");

        if (!string.IsNullOrEmpty(d.FakeUnknownUdp))
            args.Add($"--dpi-desync-fake-unknown-udp={Quote(ResolveBin(d.FakeUnknownUdp))}");

        if (!string.IsNullOrEmpty(d.FakeHttp))
            args.Add($"--dpi-desync-fake-http={Quote(ResolveBin(d.FakeHttp))}");

        if (!string.IsNullOrEmpty(d.FakeDiscord))
            args.Add($"--dpi-desync-fake-discord={Quote(ResolveBin(d.FakeDiscord))}");

        if (!string.IsNullOrEmpty(d.FakeStun))
            args.Add($"--dpi-desync-fake-stun={Quote(ResolveBin(d.FakeStun))}");

        if (!string.IsNullOrEmpty(d.FakeDht))
            args.Add($"--dpi-desync-fake-dht={Quote(ResolveBin(d.FakeDht))}");

        if (!string.IsNullOrEmpty(d.FakeWireguard))
            args.Add($"--dpi-desync-fake-wireguard={Quote(ResolveBin(d.FakeWireguard))}");

        if (d.AnyProtocol == true)
            args.Add("--dpi-desync-any-protocol=1");

        if (d.AutoTtl == true && string.IsNullOrEmpty(d.AutoTtlDelta))
        {
            args.Add("--dpi-desync-autottl");
        }
        else if (!string.IsNullOrEmpty(d.AutoTtlDelta))
        {
            args.Add($"--dpi-desync-autottl={d.AutoTtlDelta}");
        }
    }

    private void EmitAdvanced(List<string> args, DesyncAdvanced a)
    {
        if (!string.IsNullOrEmpty(a.WsSize))    args.Add($"--wssize={a.WsSize}");
        if (a.WScale is int ws)                 args.Add($"--wscale={ws}");

        if (!string.IsNullOrEmpty(a.OrigMod))   args.Add($"--orig-mod={a.OrigMod}");
        if (a.OrigAutoTtl == true && string.IsNullOrEmpty(a.OrigAutoTtlDelta))
            args.Add("--orig-autottl");
        else if (!string.IsNullOrEmpty(a.OrigAutoTtlDelta))
            args.Add($"--orig-autottl={a.OrigAutoTtlDelta}");

        if (a.Dup is int dup)                   args.Add($"--dup={dup}");
        if (!string.IsNullOrEmpty(a.DupReplace)) args.Add($"--dup-replace={a.DupReplace}");
        if (a.DupFooling is { Count: > 0 })     args.Add($"--dup-fooling={string.Join(',', a.DupFooling)}");
        if (a.DupTtl is int dt)                 args.Add($"--dup-ttl={dt}");
        if (a.DupTtl6 is int dt6)               args.Add($"--dup-ttl6={dt6}");
        if (a.DupAutoTtl == true && string.IsNullOrEmpty(a.DupAutoTtlDelta))
            args.Add("--dup-autottl");
        else if (!string.IsNullOrEmpty(a.DupAutoTtlDelta))
            args.Add($"--dup-autottl={a.DupAutoTtlDelta}");
        if (!string.IsNullOrEmpty(a.DupCutoff)) args.Add($"--dup-cutoff={a.DupCutoff}");
        if (!string.IsNullOrEmpty(a.DupStart))  args.Add($"--dup-start={a.DupStart}");

        if (a.UdpLenIncrement is int uli)       args.Add($"--udplen-increment={uli}");
        if (!string.IsNullOrEmpty(a.UdpLenPattern)) args.Add($"--udplen-pattern={a.UdpLenPattern}");

        if (!string.IsNullOrEmpty(a.CtrackTimeouts)) args.Add($"--ctrack-timeouts={a.CtrackTimeouts}");
        if (!string.IsNullOrEmpty(a.HostlistAuto))   args.Add($"--hostlist-auto={Quote(a.HostlistAuto)}");
        if (a.HostlistAutoFailThreshold is int hft)  args.Add($"--hostlist-auto-fail-threshold={hft}");
        if (a.HostlistAutoFailTime is int hftime)    args.Add($"--hostlist-auto-fail-time={hftime}");
        if (a.HostlistAutoRetransThreshold is int hrt) args.Add($"--hostlist-auto-retrans-threshold={hrt}");

        if (!string.IsNullOrEmpty(a.Cutoff))    args.Add($"--dpi-desync-cutoff={a.Cutoff}");
        if (!string.IsNullOrEmpty(a.Start))     args.Add($"--dpi-desync-start={a.Start}");

        if (!string.IsNullOrEmpty(a.FakePattern)) args.Add($"--dpi-desync-fake-pattern={a.FakePattern}");
        if (a.FakeSeqOfs is int fso)              args.Add($"--dpi-desync-fake-seqofs={fso}");
        if (!string.IsNullOrEmpty(a.FakePos))     args.Add($"--dpi-desync-fake-pos={a.FakePos}");

        if (a.UdpFakeSeqLen is int ufsl)          args.Add($"--dpi-desync-udp-fake-seqlen={ufsl}");

        if (!string.IsNullOrEmpty(a.FakeTlsMod))         args.Add($"--dpi-desync-fake-tls-mod={a.FakeTlsMod}");
        if (a.BadseqIncrement is int bsi)                args.Add($"--dpi-desync-badseq-increment={bsi}");
        if (!string.IsNullOrEmpty(a.HostFakeSplitMod))   args.Add($"--dpi-desync-hostfakesplit-mod={a.HostFakeSplitMod}");
        if (!string.IsNullOrEmpty(a.FakedSplitPattern))  args.Add($"--dpi-desync-fakedsplit-pattern={a.FakedSplitPattern}");

        if (a.RawArgs is { Count: > 0 })
        {
            args.AddRange(a.RawArgs);
        }
    }

    private string ResolveList(ListRef r, string filePrefix)
    {
        if (r.Path is not null) return r.Path;
        if (r.Pack is null) throw new InvalidOperationException("ListRef has neither pack nor path");

        string fname = r.Pack.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)
            ? r.Pack
            : $"{filePrefix}{r.Pack}.txt";

        return NormalizePath(_opt.ListsDir + fname);
    }

    private string ResolveBin(string pathOrBuiltin)
    {
        if (pathOrBuiltin.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))
        {
            string rel = pathOrBuiltin.Substring("bin/".Length);
            return NormalizePath(_opt.BinDir + rel);
        }
        return pathOrBuiltin;
    }

    private string Subst(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        string result = value;
        if (_opt.GameTcpPorts is not null)
            result = result.Replace("{game_tcp}", _opt.GameTcpPorts, StringComparison.Ordinal);
        if (_opt.GameUdpPorts is not null)
            result = result.Replace("{game_udp}", _opt.GameUdpPorts, StringComparison.Ordinal);
        return result;
    }

    private static string NormalizePath(string p) => p.Replace('/', '\\');

    private string Quote(string value)
    {
        if (!_opt.QuoteAllArgs && !value.Contains(' ')) return value;
        if (value.StartsWith('"') && value.EndsWith('"')) return value;
        return $"\"{value}\"";
    }
}
