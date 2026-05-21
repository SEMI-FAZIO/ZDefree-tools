using System.Text.Json;
using System.Text.Json.Serialization;
using ZDefree.Core.Modules;
using ZDefree.Core.Serialization;

namespace ZDefree.Core.Tests;

public class ModuleRegistryTests
{
    private static (string root, IDisposable cleanup) MakeRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zdefree-mod-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "common"));
        return (root, new DeleteOnDispose(root));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void ListAll_returns_empty_when_modules_dir_missing()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            Assert.Empty(ModuleRegistry.ListAll(root));
        }
    }

    [Fact]
    public void Save_then_Find_roundtrips_a_module_definition()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            var def = new ModuleDefinition
            {
                Name = "community-discord",
                Version = "1.0.0",
                Source = "/some/local/path",
                SourceKind = ModuleSourceKind.Local,
                Enabled = true,
                Trusted = false,
            };
            ModuleRegistry.Save(root, def);

            var loaded = ModuleRegistry.Find(root, "community-discord");
            Assert.NotNull(loaded);
            Assert.Equal("community-discord", loaded!.Definition.Name);
            Assert.Equal("1.0.0", loaded.Definition.Version);
            Assert.Equal(ModuleSourceKind.Local, loaded.Definition.SourceKind);
            Assert.False(loaded.Definition.Trusted);
        }
    }

    [Fact]
    public void Save_rejects_invalid_module_name()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            var bad = new ModuleDefinition { Name = "Bad NAME WITH SPACES" };
            Assert.Throws<ArgumentException>(() => ModuleRegistry.Save(root, bad));
        }
    }

    [Fact]
    public void ListAll_skips_dirs_without_module_json_and_records_warning()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            Directory.CreateDirectory(Path.Combine(root, "modules", "no-meta"));
            var warnings = new List<string>();
            var found = ModuleRegistry.ListAll(root, warnings);
            Assert.Empty(found);
            Assert.Contains(warnings, w => w.Contains("no-meta"));
        }
    }

    [Fact]
    public void ListAll_skips_modules_with_mismatched_name_in_module_json()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            string moduleDir = Path.Combine(root, "modules", "actual-dirname");
            Directory.CreateDirectory(moduleDir);
            var def = new ModuleDefinition { Name = "lying-name", Version = "1.0.0" };
            File.WriteAllText(Path.Combine(moduleDir, "module.json"),
                JsonSerializer.Serialize(def, JsonOpts));

            var warnings = new List<string>();
            var found = ModuleRegistry.ListAll(root, warnings);
            Assert.Empty(found);
            Assert.Contains(warnings, w => w.Contains("name") && w.Contains("doesn't match"));
        }
    }

    [Fact]
    public void Index_includes_strategies_from_enabled_trusted_module()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            // Base catalog
            File.WriteAllText(Path.Combine(root, "common", "base.json"), Strat("base", "Base", "general"));

            // Module
            string modDir = Path.Combine(root, "modules", "ext-pack");
            Directory.CreateDirectory(Path.Combine(modDir, "common"));
            File.WriteAllText(Path.Combine(modDir, "common", "ext.json"), Strat("ext", "Ext", "alt"));
            ModuleRegistry.Save(root, new ModuleDefinition
            {
                Name = "ext-pack", Version = "1.0.0", Trusted = true, Enabled = true,
            });

            var idx = StrategyIndexBuilder.Build(root, "test", out var warnings);

            Assert.Equal(2, idx.Strategies.Count);
            Assert.Contains(idx.Strategies, e => e.Id == "base" && e.Source == "core");
            Assert.Contains(idx.Strategies, e => e.Id == "ext"  && e.Source == "ext-pack");
            Assert.Empty(warnings);
        }
    }

    [Fact]
    public void Index_excludes_untrusted_module_and_surfaces_warning()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(root, "common", "base.json"), Strat("base", "Base", "general"));
            string modDir = Path.Combine(root, "modules", "untrusted-pack");
            Directory.CreateDirectory(Path.Combine(modDir, "common"));
            File.WriteAllText(Path.Combine(modDir, "common", "ext.json"), Strat("ext", "Ext", "alt"));
            ModuleRegistry.Save(root, new ModuleDefinition
            {
                Name = "untrusted-pack", Version = "1.0.0", Trusted = false, Enabled = true,
            });

            var idx = StrategyIndexBuilder.Build(root, "test", out var warnings);

            Assert.Single(idx.Strategies);
            Assert.Equal("base", idx.Strategies[0].Id);
            Assert.Contains(warnings, w => w.Contains("untrusted-pack") && w.Contains("not trusted"));
        }
    }

    [Fact]
    public void Index_skips_disabled_module_silently()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(root, "common", "base.json"), Strat("base", "Base", "general"));
            string modDir = Path.Combine(root, "modules", "off-pack");
            Directory.CreateDirectory(Path.Combine(modDir, "common"));
            File.WriteAllText(Path.Combine(modDir, "common", "ext.json"), Strat("ext", "Ext", "alt"));
            ModuleRegistry.Save(root, new ModuleDefinition
            {
                Name = "off-pack", Version = "1.0.0", Trusted = true, Enabled = false,
            });

            var idx = StrategyIndexBuilder.Build(root, "test", out var warnings);
            Assert.Single(idx.Strategies);
            Assert.DoesNotContain(warnings, w => w.Contains("off-pack"));
        }
    }

    [Fact]
    public void Index_resolves_id_conflict_core_first()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(root, "common", "shared.json"), Strat("shared", "Core Version", "general"));
            string modDir = Path.Combine(root, "modules", "conflict-pack");
            Directory.CreateDirectory(Path.Combine(modDir, "common"));
            File.WriteAllText(Path.Combine(modDir, "common", "shared.json"), Strat("shared", "Module Version", "alt"));
            ModuleRegistry.Save(root, new ModuleDefinition
            {
                Name = "conflict-pack", Version = "1.0.0", Trusted = true, Enabled = true,
            });

            var idx = StrategyIndexBuilder.Build(root, "test", out var warnings);

            var winner = idx.Strategies.Single(e => e.Id == "shared");
            Assert.Equal("Core Version", winner.Name);
            Assert.Equal("core",         winner.Source);
            Assert.Contains(warnings, w => w.Contains("shared") && w.Contains("already taken"));
        }
    }

    [Fact]
    public void Index_records_module_file_path_with_modules_prefix()
    {
        var (root, cleanup) = MakeRoot();
        using (cleanup)
        {
            string modDir = Path.Combine(root, "modules", "ext-pack");
            Directory.CreateDirectory(Path.Combine(modDir, "common"));
            File.WriteAllText(Path.Combine(modDir, "common", "ext.json"), Strat("ext", "Ext", "alt"));
            ModuleRegistry.Save(root, new ModuleDefinition
            {
                Name = "ext-pack", Version = "1.0.0", Trusted = true, Enabled = true,
            });

            var idx = StrategyIndexBuilder.Build(root, "test", out _);
            Assert.Equal("modules/ext-pack/common/ext.json", idx.Strategies.Single().File);
        }
    }

    private static string Strat(string id, string name, string cat) => $$"""
        {
          "id": "{{id}}",
          "name": "{{name}}",
          "category": "{{cat}}",
          "version": 1,
          "filters": [{
            "name": "main", "wf": { "tcp": "443" },
            "rules": [{ "match": { "tcp": "443" }, "desync": { "mode": "fake" } }]
          }]
        }
        """;

    private sealed class DeleteOnDispose : IDisposable
    {
        private readonly string _path;
        public DeleteOnDispose(string p) { _path = p; }
        public void Dispose() { try { Directory.Delete(_path, recursive: true); } catch { } }
    }
}
