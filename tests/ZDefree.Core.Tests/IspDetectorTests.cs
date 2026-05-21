using ZDefree.Core.Probing;

namespace ZDefree.Core.Tests;

public class IspDetectorTests
{
    [Fact]
    public void Parse_extracts_asn_and_org_name_from_standard_ipinfo_response()
    {
        const string json = """
        {
          "ip": "85.140.1.1",
          "city": "Moscow",
          "region": "Moscow",
          "country": "RU",
          "loc": "55.7522,37.6156",
          "org": "AS8402 PJSC Vimpelcom Communications Ltd"
        }
        """;

        var info = IspDetector.ParseIpinfoResponse(json);
        Assert.Equal("85.140.1.1", info.Ip);
        Assert.Equal("RU", info.Country);
        Assert.Equal("AS8402", info.Asn);
        Assert.Equal("PJSC Vimpelcom Communications Ltd", info.OrgName);
    }

    [Fact]
    public void Parse_keeps_raw_org_field_unchanged()
    {
        const string json = """
        { "ip": "1.1.1.1", "org": "AS13335 Cloudflare, Inc." }
        """;
        var info = IspDetector.ParseIpinfoResponse(json);
        Assert.Equal("AS13335 Cloudflare, Inc.", info.Raw);
    }

    [Fact]
    public void Parse_handles_missing_org_field()
    {
        const string json = """ { "ip": "1.2.3.4", "country": "US" } """;
        var info = IspDetector.ParseIpinfoResponse(json);
        Assert.Equal("1.2.3.4", info.Ip);
        Assert.Null(info.Asn);
        Assert.Null(info.OrgName);
        Assert.Null(info.Raw);
    }

    [Fact]
    public void Parse_handles_org_without_asn_prefix()
    {
        const string json = """ { "ip": "1.2.3.4", "org": "Just an ISP name" } """;
        var info = IspDetector.ParseIpinfoResponse(json);
        Assert.Null(info.Asn);
        Assert.Equal("Just an ISP name", info.OrgName);
    }

    [Fact]
    public void CompatTag_combines_asn_with_first_significant_org_word()
    {
        var info = new IspInfo("x", "RU", "AS8402", "PJSC Vimpelcom Communications Ltd", "raw");
        Assert.Equal("AS8402-PJSC", info.CompatTag);
    }

    [Fact]
    public void CompatTag_skips_short_first_word_takes_next()
    {
        var info = new IspInfo("x", "RU", "AS12389", "PJ Rostelecom", "raw");
        // "PJ" is 2 chars - too short; "Rostelecom" picked.
        Assert.Equal("AS12389-Rostelecom", info.CompatTag);
    }

    [Fact]
    public void CompatTag_returns_null_without_asn()
    {
        var info = new IspInfo("x", "RU", null, "Some Org", "raw");
        Assert.Null(info.CompatTag);
    }

    [Fact]
    public void CompatTag_returns_bare_asn_when_org_unparseable()
    {
        var info = new IspInfo("x", "RU", "AS123", null, "raw");
        Assert.Equal("AS123", info.CompatTag);
    }
}
