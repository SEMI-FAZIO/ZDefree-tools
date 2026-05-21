using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZDefree.Core.Models;
using ZDefree.Core.Modules;

namespace ZDefree.Core.Serialization;

public sealed class StrategyIndexEntry
{
    [JsonPropertyName("id")]          public string     Id          { get; init; } = "";
    [JsonPropertyName("name")]        public string     Name        { get; init; } = "";
    [JsonPropertyName("category")]    public string?    Category    { get; init; }
    [JsonPropertyName("description")] public I18nText?  Description { get; init; }
    [JsonPropertyName("file")]        public string     File        { get; init; } = "";

    /// <summary>
    /// Origin: <c>"core"</c> for base catalog strategies, otherwise the module name.
    /// Added in schema_version 2 — older readers ignore it.
    /// </summary>
    [JsonPropertyName("source")]      public string?    Source      { get; init; }
}

public sealed class StrategyIndex
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; } = "INDEX.schema.json";

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("generated_at")]
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("generator")]
    public string Generator { get; set; } = "";

    [JsonPropertyName("strategies")]
    public List<StrategyIndexEntry> Strategies { get; set; } = new();
}

public static class StrategyIndexBuilder
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented              = true,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
        Encoder                    = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static StrategyIndex Build(string strategiesRoot, string generator)
        => Build(strategiesRoot, generator, out _);

    /// <summary>
    /// Composes the catalog from base <c>common/</c>+<c>advanced/</c> plus every
    /// enabled+trusted module under <c>modules/&lt;name&gt;/</c>. Conflicts
    /// (same id) are resolved core-first, then module name asc; collisions are
    /// reported via <paramref name="warnings"/>.
    /// </summary>
    public static StrategyIndex Build(string strategiesRoot, string generator, out IReadOnlyList<string> warnings)
    {
        if (!Directory.Exists(strategiesRoot))
        {
            throw new DirectoryNotFoundException($"strategies root not found: {strategiesRoot}");
        }

        var entries = new List<StrategyIndexEntry>();
        var ids     = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnList = new List<string>();

        // 1. Base catalog (source = "core")
        AddFrom(strategiesRoot, basePath: strategiesRoot, source: "core", entries, ids, warnList,
                pathInIndex: subPath => subPath);

        // 2. Modules (source = module name) — enabled + trusted only.
        var modules = ModuleRegistry.ListAll(strategiesRoot, warnList);
        foreach (var m in modules.Where(m => m.Definition.Enabled && m.Definition.Trusted))
        {
            string moduleStrategies = m.Path; // strategies live under modules/<name>/{common,advanced}
            // The "file" field is relative to the strategies root, so it's "modules/<name>/<subdir>/<file>".
            string moduleRel = Path.Combine("modules", m.Definition.Name).Replace('\\', '/');
            AddFrom(strategiesRoot, basePath: moduleStrategies, source: m.Definition.Name, entries, ids, warnList,
                    pathInIndex: subPath => $"{moduleRel}/{subPath}");
        }

        // Untrusted-but-enabled modules: surface a warning so the user sees why they aren't included.
        foreach (var m in modules.Where(m => m.Definition.Enabled && !m.Definition.Trusted))
        {
            warnList.Add($"Module '{m.Definition.Name}' is enabled but not trusted — strategies excluded from INDEX. Run `zdefree module trust {m.Definition.Name}` to include.");
        }

        entries.Sort((a, b) =>
        {
            int c = string.Compare(a.Category ?? "zz", b.Category ?? "zz", StringComparison.Ordinal);
            return c != 0 ? c : string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        });

        warnings = warnList;
        return new StrategyIndex
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            Generator   = generator,
            Strategies  = entries,
        };
    }

    private static void AddFrom(
        string strategiesRoot,
        string basePath,
        string source,
        List<StrategyIndexEntry> entries,
        HashSet<string> ids,
        List<string> warnings,
        Func<string, string> pathInIndex)
    {
        foreach (var subdir in new[] { "common", "advanced" })
        {
            string dir = Path.Combine(basePath, subdir);
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                string name = Path.GetFileName(file);
                if (name.Equals("INDEX.json", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.EndsWith(".schema.json", StringComparison.OrdinalIgnoreCase)) continue;

                Strategy s;
                try { s = StrategyLoader.LoadFromFile(file); }
                catch (Exception ex) { warnings.Add($"[{source}] {subdir}/{name} failed to load: {ex.Message}"); continue; }

                if (!ids.Add(s.Id))
                {
                    warnings.Add($"[{source}] id '{s.Id}' already taken by an earlier source — skipped.");
                    continue;
                }

                entries.Add(new StrategyIndexEntry
                {
                    Id          = s.Id,
                    Name        = s.Name,
                    Category    = s.Category,
                    Description = s.Description,
                    File        = pathInIndex($"{subdir}/{name}"),
                    Source      = source,
                });
            }
        }
    }

    public static string Serialize(StrategyIndex index)
    {
        return JsonSerializer.Serialize(index, Options);
    }

    public static StrategyIndex Deserialize(string json)
    {
        return JsonSerializer.Deserialize<StrategyIndex>(json, Options)
            ?? throw new InvalidDataException("Index parsed to null");
    }

    /// <summary>
    /// True if two indexes have the same strategy set (id, name, category, file, description),
    /// ignoring generated_at / generator / schema_version.
    /// </summary>
    public static bool Equivalent(StrategyIndex a, StrategyIndex b)
    {
        if (a.Strategies.Count != b.Strategies.Count) return false;
        for (int i = 0; i < a.Strategies.Count; i++)
        {
            var x = a.Strategies[i];
            var y = b.Strategies[i];
            if (x.Id != y.Id) return false;
            if (x.Name != y.Name) return false;
            if (x.Category != y.Category) return false;
            if (x.File != y.File) return false;
            if (x.Source != y.Source) return false;
            if (!I18nEquivalent(x.Description, y.Description)) return false;
        }
        return true;
    }

    private static bool I18nEquivalent(I18nText? a, I18nText? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.En != b.En) return false;
        if (a.Ru != b.Ru) return false;
        var aOther = a.Other ?? new();
        var bOther = b.Other ?? new();
        if (aOther.Count != bOther.Count) return false;
        foreach (var kv in aOther)
        {
            if (!bOther.TryGetValue(kv.Key, out var v)) return false;
            if (v != kv.Value) return false;
        }
        return true;
    }
}
