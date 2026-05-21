using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ZDefree.Core.Modules;

public sealed record LoadedModule(string Path, ModuleDefinition Definition);

/// <summary>
/// Disk-level registry for strategy-pack modules. Lives under
/// <c>strategies/modules/&lt;name&gt;/</c>. Each subdirectory containing
/// a <c>module.json</c> file is a module.
/// </summary>
public static class ModuleRegistry
{
    /// <summary>Module name regex: lowercase letters, digits, hyphen, underscore. Same shape as strategy id.</summary>
    public static readonly Regex NameRegex = new(@"^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented              = true,
        PropertyNameCaseInsensitive = false,
        DefaultIgnoreCondition     = JsonIgnoreCondition.WhenWritingNull,
        Converters                 = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    /// <summary>Path to modules/ inside the given strategies root.</summary>
    public static string ModulesDir(string strategiesRoot) =>
        Path.Combine(strategiesRoot, "modules");

    public static string ModulePath(string strategiesRoot, string moduleName) =>
        Path.Combine(ModulesDir(strategiesRoot), moduleName);

    public static string ModuleDefinitionPath(string strategiesRoot, string moduleName) =>
        Path.Combine(ModulePath(strategiesRoot, moduleName), "module.json");

    /// <summary>
    /// Enumerates all modules on disk. Skips directories whose <c>module.json</c>
    /// is missing or invalid (returns them as warnings via the optional <paramref name="warnings"/> list).
    /// </summary>
    public static IReadOnlyList<LoadedModule> ListAll(string strategiesRoot, List<string>? warnings = null)
    {
        string dir = ModulesDir(strategiesRoot);
        if (!Directory.Exists(dir)) return Array.Empty<LoadedModule>();

        var result = new List<LoadedModule>();
        foreach (var moduleDir in Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(moduleDir);
            string defPath = Path.Combine(moduleDir, "module.json");
            if (!File.Exists(defPath))
            {
                warnings?.Add($"Module dir '{name}' has no module.json — skipped.");
                continue;
            }

            ModuleDefinition def;
            try
            {
                def = JsonSerializer.Deserialize<ModuleDefinition>(File.ReadAllText(defPath), JsonOpts)
                    ?? throw new InvalidDataException("module.json deserialized to null");
            }
            catch (Exception ex)
            {
                warnings?.Add($"Module '{name}': module.json invalid ({ex.Message}) — skipped.");
                continue;
            }

            if (!string.Equals(def.Name, name, StringComparison.Ordinal))
            {
                warnings?.Add($"Module '{name}': module.json has name '{def.Name}' which doesn't match directory — skipped.");
                continue;
            }

            if (!NameRegex.IsMatch(name))
            {
                warnings?.Add($"Module '{name}': name violates [a-z0-9][a-z0-9_-]* — skipped.");
                continue;
            }

            result.Add(new LoadedModule(moduleDir, def));
        }
        return result;
    }

    public static LoadedModule? Find(string strategiesRoot, string moduleName)
    {
        if (!NameRegex.IsMatch(moduleName)) return null;
        string defPath = ModuleDefinitionPath(strategiesRoot, moduleName);
        if (!File.Exists(defPath)) return null;
        try
        {
            var def = JsonSerializer.Deserialize<ModuleDefinition>(File.ReadAllText(defPath), JsonOpts);
            if (def is null) return null;
            return new LoadedModule(ModulePath(strategiesRoot, moduleName), def);
        }
        catch { return null; }
    }

    public static void Save(string strategiesRoot, ModuleDefinition def)
    {
        if (!NameRegex.IsMatch(def.Name))
        {
            throw new ArgumentException($"Module name '{def.Name}' violates [a-z0-9][a-z0-9_-]*", nameof(def));
        }
        string dir = ModulePath(strategiesRoot, def.Name);
        Directory.CreateDirectory(dir);
        string json = JsonSerializer.Serialize(def, JsonOpts);
        File.WriteAllText(Path.Combine(dir, "module.json"), json);
    }
}
