using System.Text.Json;
using System.Text.Json.Serialization;
using ZDefree.Core.Modules;

namespace ZDefree.Core.Tests;

public class ModuleServiceTests
{
    private static (string root, string source, IDisposable cleanup) MakePair()
    {
        string root = Path.Combine(Path.GetTempPath(), $"zdefree-svc-{Guid.NewGuid():N}");
        string src  = Path.Combine(Path.GetTempPath(), $"src-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "common"));
        Directory.CreateDirectory(Path.Combine(src,  "common"));
        return (root, src, new CleanupTwo(root, src));
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    [Fact]
    public void AddLocal_copies_source_dir_and_resets_trust_to_false()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            // Source module with trusted=true pre-stamped (must be reset to false on add)
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition
                {
                    Name = "my-pack", Version = "1.2.3", Trusted = true, Enabled = true,
                }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));

            var result = ModuleService.AddLocal(root, src);

            Assert.Equal("my-pack", result.Name);
            Assert.True(File.Exists(Path.Combine(root, "modules", "my-pack", "module.json")));
            Assert.True(File.Exists(Path.Combine(root, "modules", "my-pack", "common", "x.json")));

            var loaded = ModuleRegistry.Find(root, "my-pack");
            Assert.NotNull(loaded);
            Assert.False(loaded!.Definition.Trusted);  // reset to false even if source claimed trusted=true
            Assert.True(loaded.Definition.Enabled);
            Assert.NotNull(result.Warnings);
            Assert.Contains(result.Warnings, w => w.Contains("trust"));
        }
    }

    [Fact]
    public void AddLocal_throws_when_module_already_exists()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "dup", Version = "1.0.0" }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));

            ModuleService.AddLocal(root, src);
            Assert.Throws<InvalidOperationException>(() => ModuleService.AddLocal(root, src));
        }
    }

    [Fact]
    public void AddLocal_throws_when_source_module_json_missing()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            // No module.json in src
            Assert.Throws<FileNotFoundException>(() => ModuleService.AddLocal(root, src));
        }
    }

    [Fact]
    public void AddLocal_throws_when_module_json_has_invalid_name()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "BAD NAME", Version = "1.0.0" }, JsonOpts));
            Assert.Throws<InvalidDataException>(() => ModuleService.AddLocal(root, src));
        }
    }

    [Fact]
    public void Remove_deletes_the_module_dir()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "to-remove", Version = "1.0.0" }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));

            ModuleService.AddLocal(root, src);
            string modPath = Path.Combine(root, "modules", "to-remove");
            Assert.True(Directory.Exists(modPath));

            ModuleService.Remove(root, "to-remove");
            Assert.False(Directory.Exists(modPath));
        }
    }

    [Fact]
    public void SetTrusted_toggles_the_trust_flag()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "trust-test", Version = "1.0.0" }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));
            ModuleService.AddLocal(root, src);

            Assert.False(ModuleRegistry.Find(root, "trust-test")!.Definition.Trusted);
            ModuleService.SetTrusted(root, "trust-test", true);
            Assert.True(ModuleRegistry.Find(root, "trust-test")!.Definition.Trusted);
            ModuleService.SetTrusted(root, "trust-test", false);
            Assert.False(ModuleRegistry.Find(root, "trust-test")!.Definition.Trusted);
        }
    }

    [Fact]
    public void SetEnabled_toggles_the_enabled_flag()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "enabled-test", Version = "1.0.0" }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));
            ModuleService.AddLocal(root, src);

            Assert.True(ModuleRegistry.Find(root, "enabled-test")!.Definition.Enabled);
            ModuleService.SetEnabled(root, "enabled-test", false);
            Assert.False(ModuleRegistry.Find(root, "enabled-test")!.Definition.Enabled);
        }
    }

    [Fact]
    public void List_returns_all_modules_with_status_fields()
    {
        var (root, src, cleanup) = MakePair();
        using (cleanup)
        {
            File.WriteAllText(Path.Combine(src, "module.json"),
                JsonSerializer.Serialize(new ModuleDefinition { Name = "alpha", Version = "1.0.0" }, JsonOpts));
            File.WriteAllText(Path.Combine(src, "common", "x.json"), Strat("x"));
            ModuleService.AddLocal(root, src);

            var list = ModuleService.List(root);
            Assert.Single(list);
            Assert.Equal("alpha", list[0].Name);
            Assert.Equal("1.0.0", list[0].Version);
            Assert.True(list[0].Enabled);
            Assert.False(list[0].Trusted);
        }
    }

    private static string Strat(string id) => $$"""
        {
          "id": "{{id}}",
          "name": "X",
          "version": 1,
          "filters": [{
            "name": "main", "wf": { "tcp": "443" },
            "rules": [{ "match": { "tcp": "443" }, "desync": { "mode": "fake" } }]
          }]
        }
        """;

    private sealed class CleanupTwo : IDisposable
    {
        private readonly string _a, _b;
        public CleanupTwo(string a, string b) { _a = a; _b = b; }
        public void Dispose()
        {
            try { Directory.Delete(_a, recursive: true); } catch { }
            try { Directory.Delete(_b, recursive: true); } catch { }
        }
    }
}
