using ZDefree.Core.Compilation;
using ZDefree.Core.Models;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class NfqwsCompilerTests
{
    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Compile_starts_with_qnum_flag()
    {
        var s = SimpleStrategy();
        string cli = new NfqwsCompiler(new NfqwsCompileOptions { QueueNum = 200 }).Compile(s);
        Assert.StartsWith("--qnum=200", cli);
    }

    [Fact]
    public void Compile_omits_wf_tcp_and_wf_udp_flags()
    {
        var s = new Strategy
        {
            Id = "t", Name = "t", Version = 1,
            Filters = { new FilterGroup { Name = "main", Wf = new WfBlock { Tcp = "443", Udp = "443" },
                Rules = { new Rule { Match = new MatchBlock { Tcp = "443" }, Desync = new DesyncCommon { Mode = "fake" } } } } }
        };
        string cli = new NfqwsCompiler().Compile(s);
        Assert.DoesNotContain("--wf-tcp", cli);
        Assert.DoesNotContain("--wf-udp", cli);
        Assert.DoesNotContain("--wf-iface", cli);
    }

    [Fact]
    public void Compile_emits_per_rule_filter_flags_same_as_winws()
    {
        var s = SimpleStrategy();
        string cli = new NfqwsCompiler().Compile(s);
        Assert.Contains("--filter-tcp=443", cli);
        Assert.Contains("--dpi-desync=fake", cli);
    }

    [Fact]
    public void Compile_uses_forward_slashes_for_paths()
    {
        var s = new Strategy
        {
            Id = "t", Name = "t", Version = 1,
            Filters = { new FilterGroup { Name = "main", Wf = new WfBlock { Tcp = "443" },
                Rules = { new Rule
                {
                    Match = new MatchBlock { Tcp = "443" },
                    Hostlist = new() { new ListRef { Pack = "general" } },
                    Desync = new DesyncCommon { Mode = "fake", FakeTls = "bin/quic_initial.bin" },
                } } } }
        };
        string cli = new NfqwsCompiler(new NfqwsCompileOptions
        {
            BinDir = "/etc/zapret/bin/", ListsDir = "/etc/zapret/lists/",
        }).Compile(s);

        Assert.Contains("/etc/zapret/lists/list-general.txt", cli);
        Assert.Contains("/etc/zapret/bin/quic_initial.bin", cli);
        Assert.DoesNotContain("\\", cli);
    }

    [Fact]
    public void Compile_substitutes_game_tcp_and_game_udp_templates()
    {
        var s = new Strategy
        {
            Id = "t", Name = "t", Version = 1,
            Filters = { new FilterGroup { Name = "main", Wf = new WfBlock { Tcp = "443" },
                Rules = { new Rule { Match = new MatchBlock { Tcp = "{game_tcp}", Udp = "{game_udp}" },
                    Desync = new DesyncCommon { Mode = "fake" } } } } }
        };
        string cli = new NfqwsCompiler(new NfqwsCompileOptions
        {
            GameTcpPorts = "27015,27036", GameUdpPorts = "3478-3480",
        }).Compile(s);

        Assert.Contains("--filter-tcp=27015,27036", cli);
        Assert.Contains("--filter-udp=3478-3480", cli);
    }

    [Fact]
    public void Compile_separates_rules_with_new_token()
    {
        var s = new Strategy
        {
            Id = "t", Name = "t", Version = 1,
            Filters = { new FilterGroup { Name = "main", Wf = new WfBlock { Tcp = "443" },
                Rules =
                {
                    new Rule { Match = new MatchBlock { Tcp = "443" }, Desync = new DesyncCommon { Mode = "fake" } },
                    new Rule { Match = new MatchBlock { Tcp = "80"  }, Desync = new DesyncCommon { Mode = "split" } },
                } } }
        };
        string cli = new NfqwsCompiler().Compile(s);
        Assert.Contains("--new", cli);
        Assert.Single(cli.Split("--new", StringSplitOptions.RemoveEmptyEntries),
            seg => seg.Contains("--filter-tcp=443") && seg.Contains("--dpi-desync=fake"));
    }

    [Fact]
    public void Compile_handwritten_general_json_fixture_runs_clean()
    {
        // Re-use the same general.json fixture that WinwsCompiler tests use —
        // smoke that all flag paths emit without throwing.
        var s = StrategyLoader.LoadFromFile(Fixture("general.json"));
        string cli = new NfqwsCompiler(new NfqwsCompileOptions
        {
            GameTcpPorts = "27015", GameUdpPorts = "3478",
        }).Compile(s);

        Assert.StartsWith("--qnum=200", cli);
        Assert.Contains("--dpi-desync", cli);
        Assert.DoesNotContain("--wf-tcp", cli);  // nfqws-specific
        Assert.DoesNotContain("\\",       cli);  // forward-slash only
    }

    [Fact]
    public void Compile_uses_custom_queue_number_when_specified()
    {
        string cli = new NfqwsCompiler(new NfqwsCompileOptions { QueueNum = 537 }).Compile(SimpleStrategy());
        Assert.StartsWith("--qnum=537", cli);
    }

    private static Strategy SimpleStrategy() => new()
    {
        Id = "t", Name = "t", Version = 1,
        Filters = { new FilterGroup { Name = "main", Wf = new WfBlock { Tcp = "443" },
            Rules = { new Rule { Match = new MatchBlock { Tcp = "443" }, Desync = new DesyncCommon { Mode = "fake" } } } } }
    };
}
