namespace ZDefree.Core.Bootstrap;

public enum WinwsArch
{
    Auto,
    X64,
    X86,
    Arm64,
}

public sealed class BootstrapOptions
{
    public required string TargetDir { get; init; }

    public bool DownloadWinws { get; init; } = true;
    public bool DownloadWinDivert { get; init; } = true;
    public bool DownloadPatterns { get; init; } = true;
    public bool DownloadLists { get; init; } = true;

    public WinwsArch Arch { get; init; } = WinwsArch.Auto;

    public IProgress<BootstrapProgress>? Progress { get; init; }
}

public sealed record BootstrapProgress(string Component, string Stage, int? PercentComplete = null);

public sealed class BootstrapResult
{
    public required string TargetDir { get; init; }
    public string? WinwsVersion { get; set; }
    public string? WinDivertVersion { get; set; }
    public int PatternsInstalled { get; set; }
    public List<string> InstalledFiles { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}
