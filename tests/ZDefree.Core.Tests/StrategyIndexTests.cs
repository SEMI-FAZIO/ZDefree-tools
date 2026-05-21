using ZDefree.Core.Models;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class StrategyIndexTests
{
    private static string IndexFixtureRoot() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "index");

    [Fact]
    public void Build_emits_one_entry_per_strategy_file()
    {
        var idx = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        Assert.Equal(3, idx.Strategies.Count);
    }

    [Fact]
    public void Build_preserves_i18n_description_object()
    {
        var idx = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        var withI18n = idx.Strategies.Single(s => s.Id == "with-i18n");

        Assert.NotNull(withI18n.Description);
        Assert.Equal("English text", withI18n.Description!.En);
        Assert.Equal("Русский текст", withI18n.Description.Ru);
    }

    [Fact]
    public void Build_sorts_by_category_then_id_deterministically()
    {
        var idx1 = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        var idx2 = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");

        var ids1 = idx1.Strategies.Select(s => s.Id).ToList();
        var ids2 = idx2.Strategies.Select(s => s.Id).ToList();
        Assert.Equal(ids1, ids2);

        // alt < general < (uncategorized 'zz' sort key)
        Assert.Equal(new[] { "alt-one", "general-one", "with-i18n" }, ids1);
    }

    [Fact]
    public void Build_excludes_INDEX_json_and_schema_files_from_scan()
    {
        // The fixture root contains an INDEX.json and a noise.schema.json; both must be skipped.
        var idx = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        Assert.DoesNotContain(idx.Strategies, s => s.Id == "INDEX");
        Assert.DoesNotContain(idx.Strategies, s => s.File.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_records_file_as_subdir_slash_filename()
    {
        var idx = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        var alt = idx.Strategies.Single(s => s.Id == "alt-one");
        Assert.Equal("common/alt-one.json", alt.File);
    }

    [Fact]
    public void Serialize_emits_pretty_json_that_deserializes_back()
    {
        var idx = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test gen");
        string json = StrategyIndexBuilder.Serialize(idx);

        Assert.Contains("\"strategies\"", json);
        Assert.Contains("\n", json); // pretty-printed
        var round = StrategyIndexBuilder.Deserialize(json);
        Assert.Equal(idx.Strategies.Count, round.Strategies.Count);
        Assert.Equal("test gen", round.Generator);
    }

    [Fact]
    public void Equivalent_ignores_generated_at_and_generator()
    {
        var a = StrategyIndexBuilder.Build(IndexFixtureRoot(), "gen-a");
        // Re-build moments later — same content, different timestamp + generator string.
        var b = StrategyIndexBuilder.Build(IndexFixtureRoot(), "gen-b");
        b.GeneratedAt = a.GeneratedAt.AddDays(1);

        Assert.True(StrategyIndexBuilder.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_returns_false_when_strategy_set_differs()
    {
        var a = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        var b = StrategyIndexBuilder.Build(IndexFixtureRoot(), "test");
        b.Strategies.RemoveAt(0);

        Assert.False(StrategyIndexBuilder.Equivalent(a, b));
    }
}
