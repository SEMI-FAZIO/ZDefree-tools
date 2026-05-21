using System.Text.Json;

namespace ZDefree.Core.Modules;

public sealed record ModuleListItem(string Name, string Version, ModuleSourceKind SourceKind, string Source, bool Enabled, bool Trusted);

public sealed record ModuleAddResult(string Name, string Path, IReadOnlyList<string> Warnings);

/// <summary>
/// High-level operations on the modules/ registry. CLI subcommands call these.
/// Currently supports <see cref="ModuleSourceKind.Local"/> sources only; git
/// and https-archive can be added later by extending <see cref="AddLocalAsync"/>
/// with parallel methods.
/// </summary>
public static class ModuleService
{
    public static IReadOnlyList<ModuleListItem> List(string strategiesRoot, List<string>? warnings = null)
    {
        var loaded = ModuleRegistry.ListAll(strategiesRoot, warnings);
        return loaded.Select(m => new ModuleListItem(
            Name:       m.Definition.Name,
            Version:    m.Definition.Version,
            SourceKind: m.Definition.SourceKind,
            Source:     m.Definition.Source,
            Enabled:    m.Definition.Enabled,
            Trusted:    m.Definition.Trusted)).ToList();
    }

    /// <summary>
    /// Adds a module from a local directory. The source directory must contain
    /// a <c>module.json</c> with a valid name + a <c>common/</c> (or
    /// <c>advanced/</c>) subdirectory with at least one strategy JSON. The
    /// module is copied into <c>strategies/modules/&lt;name&gt;/</c> and marked
    /// <c>trusted=false</c> — user must run <c>module trust</c> before it's
    /// included in INDEX.
    /// </summary>
    public static ModuleAddResult AddLocal(string strategiesRoot, string sourcePath)
    {
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"source directory not found: {sourcePath}");
        }

        string srcDefPath = Path.Combine(sourcePath, "module.json");
        if (!File.Exists(srcDefPath))
        {
            throw new FileNotFoundException($"source has no module.json: {srcDefPath}");
        }

        ModuleDefinition def;
        using (var fs = File.OpenRead(srcDefPath))
        {
            def = JsonSerializer.Deserialize<ModuleDefinition>(fs, SnakeOpts)
                ?? throw new InvalidDataException("source module.json deserialized to null");
        }

        if (!ModuleRegistry.NameRegex.IsMatch(def.Name))
        {
            throw new InvalidDataException(
                $"source module.json has invalid name '{def.Name}' — must match {ModuleRegistry.NameRegex}");
        }

        string destPath = ModuleRegistry.ModulePath(strategiesRoot, def.Name);
        if (Directory.Exists(destPath))
        {
            throw new InvalidOperationException(
                $"module '{def.Name}' already exists at {destPath}. Remove first with `zdefree module remove {def.Name}`.");
        }

        // Copy the source directory recursively (excluding any nested .git).
        CopyDirectory(sourcePath, destPath, skipDirs: new[] { ".git", "node_modules", "bin", "obj" });

        // Stamp the metadata: record actual source, reset trust/enabled defaults, set added_at.
        def.SourceKind = ModuleSourceKind.Local;
        def.Source     = Path.GetFullPath(sourcePath);
        def.SourceRef  = Path.GetFullPath(sourcePath);
        def.AddedAt    = DateTimeOffset.UtcNow;
        def.Trusted    = false;   // TOFU — user must opt in
        def.Enabled    = true;
        ModuleRegistry.Save(strategiesRoot, def);

        var warnings = new List<string> {
            $"Module '{def.Name}' added to {destPath} but NOT trusted yet. " +
            $"Review the strategies, then run: zdefree module trust {def.Name}",
        };
        return new ModuleAddResult(def.Name, destPath, warnings);
    }

    public static void Remove(string strategiesRoot, string moduleName)
    {
        if (!ModuleRegistry.NameRegex.IsMatch(moduleName))
        {
            throw new ArgumentException($"invalid module name: {moduleName}", nameof(moduleName));
        }
        string path = ModuleRegistry.ModulePath(strategiesRoot, moduleName);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"module not found: {moduleName}");
        }
        Directory.Delete(path, recursive: true);
    }

    public static void SetTrusted(string strategiesRoot, string moduleName, bool trusted)
    {
        var loaded = ModuleRegistry.Find(strategiesRoot, moduleName)
            ?? throw new InvalidOperationException($"module not found: {moduleName}");
        loaded.Definition.Trusted = trusted;
        ModuleRegistry.Save(strategiesRoot, loaded.Definition);
    }

    public static void SetEnabled(string strategiesRoot, string moduleName, bool enabled)
    {
        var loaded = ModuleRegistry.Find(strategiesRoot, moduleName)
            ?? throw new InvalidOperationException($"module not found: {moduleName}");
        loaded.Definition.Enabled = enabled;
        ModuleRegistry.Save(strategiesRoot, loaded.Definition);
    }

    private static readonly JsonSerializerOptions SnakeOpts = new()
    {
        PropertyNameCaseInsensitive = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private static void CopyDirectory(string source, string dest, IReadOnlyCollection<string> skipDirs)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(dir);
            if (skipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            CopyDirectory(dir, Path.Combine(dest, name), skipDirs);
        }
    }
}
