using ZDefree.Core.Conversion;

namespace ZDefree.Core.Tests;

public class BatchTokenizerTests
{
    private static string FlowsealFixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "flowseal", name);

    [Fact]
    public void Parses_real_flowseal_general_bat()
    {
        var p = BatchTokenizer.Parse(FlowsealFixture("general.bat"));

        Assert.Equal("80,443,2053,2083,2087,2096,8443,{game_tcp}", p.WfTcp);
        Assert.Equal("443,19294-19344,50000-50100,{game_udp}", p.WfUdp);
        Assert.Equal(9, p.Rules.Count);
    }

    [Fact]
    public void Substitutes_BIN_and_LISTS_and_GameFilter_vars()
    {
        const string input = "winws.exe --hostlist=\"%LISTS%list-general.txt\" --dpi-desync-fake-quic=\"%BIN%google.bin\" --wf-tcp=80,%GameFilterTCP% --wf-udp=443,%GameFilterUDP%";
        var p = BatchTokenizer.ParseText(input);

        Assert.Equal("80,{game_tcp}", p.WfTcp);
        Assert.Equal("443,{game_udp}", p.WfUdp);
        Assert.Single(p.Rules);
        Assert.Contains(p.Rules[0], t => t.StartsWith("--hostlist=") && t.Contains("lists/list-general.txt"));
        Assert.Contains(p.Rules[0], t => t.StartsWith("--dpi-desync-fake-quic=") && t.Contains("bin/google.bin"));
    }

    [Fact]
    public void Tokenize_preserves_quoted_paths_with_spaces()
    {
        var tokens = BatchTokenizer.Tokenize(
            "--hostlist=\"C:/program files/lists/general.txt\" --dpi-desync=fake");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("--hostlist=C:/program files/lists/general.txt", tokens[0]);
        Assert.Equal("--dpi-desync=fake", tokens[1]);
    }

    [Fact]
    public void ExtractWinwsLine_joins_caret_continuations()
    {
        const string input = """
        @echo off
        start "" /min "%BIN%winws.exe" --wf-tcp=443 ^
        --filter-tcp=443 --dpi-desync=fake --new ^
        --filter-tcp=80 --dpi-desync=split2
        echo done
        """;

        string? line = BatchTokenizer.ExtractWinwsLine(input);
        Assert.NotNull(line);
        Assert.Contains("--wf-tcp=443", line);
        Assert.Contains("--filter-tcp=443", line);
        Assert.Contains("--new", line);
        Assert.Contains("--filter-tcp=80", line);
        Assert.DoesNotContain("echo done", line);
    }

    [Fact]
    public void Decode_falls_back_to_cp866_on_invalid_utf8()
    {
        // "Привет" in cp866:
        //   П = 0x8F, р = 0xE0, и = 0xA8, в = 0xA2, е = 0xA5, т = 0xE2.
        // 0x8F as a leading byte is invalid UTF-8 (it's a continuation byte
        // start pattern), so strict UTF-8 decode fails and we fall through
        // to the cp866 path.
        byte[] cp866Russian = { 0x8F, 0xE0, 0xA8, 0xA2, 0xA5, 0xE2 };
        string decoded = BatchTokenizer.DecodeBytes(cp866Russian);
        Assert.Equal("Привет", decoded);
    }

    [Fact]
    public void Decode_respects_utf8_bom()
    {
        byte[] utf8WithBom = { 0xEF, 0xBB, 0xBF, 0xD0, 0x9F, 0xD1, 0x80, 0xD0, 0xB8 };
        string decoded = BatchTokenizer.DecodeBytes(utf8WithBom);
        Assert.Equal("При", decoded);
    }

    [Fact]
    public void Parser_throws_when_no_winws_invocation()
    {
        const string input = "@echo off\necho nothing here";
        Assert.Throws<InvalidDataException>(() => BatchTokenizer.ParseText(input));
    }
}
