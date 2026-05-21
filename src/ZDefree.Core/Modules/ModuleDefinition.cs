using System.Text.Json.Serialization;
using ZDefree.Core.Models;

namespace ZDefree.Core.Modules;

public enum ModuleSourceKind
{
    /// <summary>The module was added from a local filesystem path (copied into modules/).</summary>
    Local,
    /// <summary>The module was cloned/pulled from a git URL.</summary>
    Git,
    /// <summary>The module was downloaded as a .zip / .tar.gz over HTTPS.</summary>
    HttpsArchive,
}

/// <summary>
/// Metadata for a single strategy-pack module. Written to
/// <c>strategies/modules/&lt;name&gt;/module.json</c>. The directory name
/// must match <see cref="Name"/> for safe disk-level isolation.
/// </summary>
public sealed class ModuleDefinition
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; } = "../../module.schema.json";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "0.0.0";

    [JsonPropertyName("source_kind")]
    public ModuleSourceKind SourceKind { get; set; } = ModuleSourceKind.Local;

    /// <summary>Origin reference: git URL, https URL, or absolute filesystem path.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Commit SHA (git), SHA-256 (https archive), or original path (local).
    /// Used by <c>zdefree module update</c> to detect upstream drift.
    /// </summary>
    [JsonPropertyName("source_ref")]
    public string? SourceRef { get; set; }

    [JsonPropertyName("added_at")]
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("description")]
    public I18nText? Description { get; set; }

    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }

    [JsonPropertyName("maintainer")]
    public string? Maintainer { get; set; }

    [JsonPropertyName("license")]
    public string? License { get; set; }

    /// <summary>
    /// Set by <c>zdefree module enable/disable</c>. Disabled modules are
    /// skipped by <c>StrategyIndexBuilder</c>.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Trust-on-first-use: a freshly added module starts as <c>false</c> and
    /// is hidden from <c>INDEX.json</c> until the user runs
    /// <c>zdefree module trust &lt;name&gt;</c>. This prevents accidental
    /// execution of unreviewed strategies.
    /// </summary>
    [JsonPropertyName("trusted")]
    public bool Trusted { get; set; } = false;
}
