using ZDefree.Core.Compilation;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class WinwsCompilerTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static string CompileFixture(string fixture, CompileOptions? opt = null)
    {
        var s = StrategyLoader.LoadFromFile(FixturePath(fixture));
        return new WinwsCompiler(opt).Compile(s);
    }

    [Fact]
    public void General_emits_top_level_wf_flags_first()
    {
        var cli = CompileFixture("general.json");

        Assert.StartsWith("--wf-tcp=", cli);
        Assert.Contains("--wf-tcp=80,443,2053,2083,2087,2096,8443,{game_tcp}", cli);
        Assert.Contains("--wf-udp=443,19294-19344,50000-50100,{game_udp}", cli);
    }

    [Fact]
    public void General_has_exactly_eight_new_separators_for_nine_rules()
    {
        var cli = CompileFixture("general.json");

        int count = cli.Split(' ').Count(t => t == "--new");
        Assert.Equal(8, count);
    }

    [Fact]
    public void General_emits_rule_one_fake_quic()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("--dpi-desync=fake", cli);
        Assert.Contains("--dpi-desync-repeats=6", cli);
        Assert.Contains("quic_initial_www_google_com.bin", cli);
    }

    [Fact]
    public void General_emits_rule_two_discord_stun_fakes()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("--filter-l7=discord,stun", cli);
        Assert.Contains("--dpi-desync-fake-discord=", cli);
        Assert.Contains("--dpi-desync-fake-stun=", cli);
        Assert.Contains("quic_initial_dbankcloud_ru.bin", cli);
    }

    [Fact]
    public void General_emits_rule_three_inline_domains()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("--hostlist-domains=discord.media", cli);
        Assert.Contains("--dpi-desync=multisplit", cli);
        Assert.Contains("--dpi-desync-split-seqovl=681", cli);
        Assert.Contains("--dpi-desync-split-pos=1", cli);
    }

    [Fact]
    public void General_emits_rule_four_ip_id_zero()
    {
        var cli = CompileFixture("general.json");
        Assert.Contains("--ip-id=zero", cli);
    }

    [Fact]
    public void General_emits_rule_five_4pda_pattern()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("--dpi-desync-split-seqovl=568", cli);
        Assert.Contains("tls_clienthello_4pda_to.bin", cli);
    }

    [Fact]
    public void General_emits_any_protocol_and_cutoffs()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("--dpi-desync-any-protocol=1", cli);
        Assert.Contains("--dpi-desync-cutoff=n3", cli);
        Assert.Contains("--dpi-desync-cutoff=n2", cli);
        Assert.Contains("--dpi-desync-repeats=12", cli);
        Assert.Contains("--dpi-desync-fake-unknown-udp=", cli);
    }

    [Fact]
    public void Template_variables_are_substituted_when_provided()
    {
        var opt = new CompileOptions
        {
            GameTcpPorts = "1024-65535",
            GameUdpPorts = "1024-65535",
        };
        var cli = CompileFixture("general.json", opt);

        Assert.Contains("--wf-tcp=80,443,2053,2083,2087,2096,8443,1024-65535", cli);
        Assert.Contains("--wf-udp=443,19294-19344,50000-50100,1024-65535", cli);
        Assert.DoesNotContain("{game_tcp}", cli);
        Assert.DoesNotContain("{game_udp}", cli);
    }

    [Fact]
    public void Template_variables_are_kept_literal_when_not_provided()
    {
        var cli = CompileFixture("general.json");

        Assert.Contains("{game_tcp}", cli);
        Assert.Contains("{game_udp}", cli);
    }

    [Fact]
    public void Bin_paths_are_resolved_with_custom_bin_dir()
    {
        var opt = new CompileOptions { BinDir = @"C:\zdefree\bin\" };
        var cli = CompileFixture("general.json", opt);

        Assert.Contains(@"C:\zdefree\bin\quic_initial_www_google_com.bin", cli);
    }

    [Fact]
    public void List_paths_are_resolved_with_custom_lists_dir()
    {
        var opt = new CompileOptions { ListsDir = @"C:\zdefree\lists\" };
        var cli = CompileFixture("general.json", opt);

        Assert.Contains(@"C:\zdefree\lists\list-general.txt", cli);
        Assert.Contains(@"C:\zdefree\lists\list-google.txt", cli);
    }

    [Fact]
    public void Empty_strategy_throws_on_compile()
    {
        const string json = """
        {
          "id": "minimal",
          "name": "Minimal",
          "filters": [
            { "wf": {"tcp": "443"}, "rules": [{"desync": {"mode": "fake"}}] }
          ]
        }
        """;
        var s = StrategyLoader.LoadFromString(json);
        var cli = new WinwsCompiler().Compile(s);
        Assert.Equal("--wf-tcp=443 --dpi-desync=fake", cli);
    }
}
