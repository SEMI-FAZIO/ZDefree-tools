using ZDefree.Core.Compilation;
using ZDefree.Core.Conversion;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class BatchToStrategyTests
{
    private static string FlowsealFixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "flowseal", name);

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Converts_flowseal_general_bat_to_nine_rule_strategy()
    {
        var result = BatchToStrategy.Convert(FlowsealFixture("general.bat"));

        var s = result.Strategy;
        Assert.Equal("general", s.Id);
        Assert.Single(s.Filters);
        Assert.Equal(9, s.Filters[0].Rules.Count);
        Assert.Equal("80,443,2053,2083,2087,2096,8443,{game_tcp}", s.Filters[0].Wf.Tcp);
        Assert.Equal("443,19294-19344,50000-50100,{game_udp}", s.Filters[0].Wf.Udp);
    }

    [Fact]
    public void Converted_rule_1_has_fake_quic_and_repeats_6()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[0];

        Assert.Equal("443", r.Match?.Udp);
        Assert.NotNull(r.Hostlist);
        Assert.Equal("general", r.Hostlist!.Pack);
        Assert.Equal("fake", r.Desync?.Mode);
        Assert.Equal(6, r.Desync?.Repeats);
        Assert.Contains("quic_initial_www_google_com.bin", r.Desync?.FakeQuic);
    }

    [Fact]
    public void Converted_rule_2_has_discord_stun_l7_filter()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[1];

        Assert.NotNull(r.Match?.L7);
        Assert.Contains("discord", r.Match!.L7!);
        Assert.Contains("stun", r.Match.L7!);
        Assert.Contains("dbankcloud_ru", r.Desync?.FakeDiscord);
        Assert.Contains("dbankcloud_ru", r.Desync?.FakeStun);
    }

    [Fact]
    public void Converted_rule_3_has_inline_hostlist_domains()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[2];

        Assert.NotNull(r.HostlistDomains);
        Assert.Contains("discord.media", r.HostlistDomains!);
        Assert.Equal("multisplit", r.Desync?.Mode);
        Assert.Equal(681, r.Desync?.SplitSeqovl);
    }

    [Fact]
    public void Converted_rule_4_has_ip_id_zero_and_google_list()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[3];

        Assert.Equal("zero", r.IpId);
        Assert.Equal("google", r.Hostlist?.Pack);
    }

    [Fact]
    public void Converted_rule_8_has_any_protocol_and_cutoff_n3()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[7];

        Assert.True(r.Desync?.AnyProtocol);
        Assert.Equal("n3", r.Advanced?.Cutoff);
        Assert.Equal("{game_tcp}", r.Match?.Tcp);
    }

    [Fact]
    public void Converted_rule_9_has_any_protocol_repeats_12_cutoff_n2()
    {
        var s = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var r = s.Filters[0].Rules[8];

        Assert.True(r.Desync?.AnyProtocol);
        Assert.Equal(12, r.Desync?.Repeats);
        Assert.Equal("n2", r.Advanced?.Cutoff);
        Assert.Equal("{game_udp}", r.Match?.Udp);
    }

    [Fact]
    public void Roundtrip_bat_to_strategy_to_cli_matches_handwritten_strategy_compile()
    {
        var convertedStrategy = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        var handwrittenStrategy = StrategyLoader.LoadFromFile(Fixture("general.json"));

        var compiler = new WinwsCompiler();
        string fromBat = compiler.Compile(convertedStrategy);
        string fromJson = compiler.Compile(handwrittenStrategy);

        Assert.Equal(fromJson, fromBat);
    }

    [Fact]
    public void Converted_strategy_serializes_to_valid_loadable_json()
    {
        var converted = BatchToStrategy.Convert(FlowsealFixture("general.bat")).Strategy;
        string json = StrategyLoader.Serialize(converted);
        var reloaded = StrategyLoader.LoadFromString(json);

        Assert.Equal(converted.Id, reloaded.Id);
        Assert.Equal(converted.Filters[0].Rules.Count, reloaded.Filters[0].Rules.Count);
    }

    [Fact]
    public void No_warnings_for_clean_flowseal_general()
    {
        var result = BatchToStrategy.Convert(FlowsealFixture("general.bat"));
        Assert.Empty(result.Warnings);
    }
}
