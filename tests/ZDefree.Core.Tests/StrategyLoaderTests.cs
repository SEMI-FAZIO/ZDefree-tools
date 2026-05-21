using ZDefree.Core.Models;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class StrategyLoaderTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    [Fact]
    public void Loads_general_json_fixture()
    {
        var s = StrategyLoader.LoadFromFile(FixturePath("general.json"));

        Assert.Equal("general", s.Id);
        Assert.Equal("General", s.Name);
        Assert.Equal("general", s.Category);
        Assert.Equal(1, s.Version);
        Assert.Single(s.Filters);
        Assert.Equal(9, s.Filters[0].Rules.Count);
    }

    [Fact]
    public void Description_parses_as_i18n_object()
    {
        var s = StrategyLoader.LoadFromFile(FixturePath("general.json"));

        Assert.NotNull(s.Description);
        Assert.NotNull(s.Description!.En);
        Assert.NotNull(s.Description.Ru);
        Assert.Contains("Default strategy", s.Description.En);
        Assert.Contains("по умолчанию", s.Description.Ru);
    }

    [Fact]
    public void Description_resolves_by_language()
    {
        var s = StrategyLoader.LoadFromFile(FixturePath("general.json"));
        Assert.Contains("по умолчанию", s.Description!.Resolve("ru"));
        Assert.Contains("Default strategy", s.Description.Resolve("en"));
        Assert.Contains("Default strategy", s.Description.Resolve("fr"));
    }

    [Fact]
    public void Description_accepts_plain_string()
    {
        const string json = """
        {
          "id": "tst",
          "name": "Test",
          "description": "just a string",
          "filters": [{"wf": {"tcp": "443"}, "rules": [{"desync": {"mode": "fake"}}]}]
        }
        """;

        var s = StrategyLoader.LoadFromString(json);
        Assert.Equal("just a string", s.Description!.En);
        Assert.Null(s.Description.Ru);
        Assert.Equal("just a string", s.Description.Resolve("ru"));
    }

    [Fact]
    public void Listref_accepts_plain_pack_name()
    {
        var s = StrategyLoader.LoadFromFile(FixturePath("general.json"));
        var rule0 = s.Filters[0].Rules[0];
        Assert.NotNull(rule0.Hostlist);
        Assert.Equal("general", rule0.Hostlist!.Pack);
    }

    [Fact]
    public void Validation_rejects_missing_id()
    {
        const string json = """
        {
          "name": "No ID",
          "filters": [{"wf": {"tcp": "443"}, "rules": [{"desync": {"mode": "fake"}}]}]
        }
        """;
        Assert.Throws<InvalidDataException>(() => StrategyLoader.LoadFromString(json));
    }

    [Fact]
    public void Validation_rejects_empty_filters()
    {
        const string json = """
        {
          "id": "x",
          "name": "X",
          "filters": []
        }
        """;
        Assert.Throws<InvalidDataException>(() => StrategyLoader.LoadFromString(json));
    }

    [Fact]
    public void Validation_rejects_filter_without_rules()
    {
        const string json = """
        {
          "id": "x",
          "name": "X",
          "filters": [{"wf": {"tcp": "443"}, "rules": []}]
        }
        """;
        Assert.Throws<InvalidDataException>(() => StrategyLoader.LoadFromString(json));
    }

    [Fact]
    public void Validation_rejects_wf_without_any_port()
    {
        const string json = """
        {
          "id": "x",
          "name": "X",
          "filters": [{"wf": {}, "rules": [{"desync": {"mode": "fake"}}]}]
        }
        """;
        Assert.Throws<InvalidDataException>(() => StrategyLoader.LoadFromString(json));
    }
}
