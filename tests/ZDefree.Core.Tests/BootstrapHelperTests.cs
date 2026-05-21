using System.IO.Compression;
using ZDefree.Core.Bootstrap;

namespace ZDefree.Core.Tests;

public class BootstrapHelperTests
{
    [Theory]
    [InlineData("https://github.com/foo/bar",                     true)]
    [InlineData("https://api.github.com/repos/foo/bar",            true)]
    [InlineData("https://raw.githubusercontent.com/foo/bar/main",  true)]
    [InlineData("https://objects.githubusercontent.com/blob",      true)]
    [InlineData("https://release-assets.githubusercontent.com/x",  true)]
    [InlineData("https://codeload.github.com/foo/bar/zip/main",    true)]
    [InlineData("http://github.com/foo/bar",                       false)]
    [InlineData("https://evil.example.com/github.com",             false)]
    [InlineData("https://github.com.attacker.tld/foo",             false)]
    [InlineData("not-a-url",                                       false)]
    [InlineData("",                                                false)]
    public void IsAllowedHost_matches_only_https_github_hosts(string url, bool expected)
    {
        Assert.Equal(expected, GitHubClient.IsAllowedHost(url));
    }

    [Fact]
    public async Task ComputeSha256_matches_known_value_for_empty_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zdf-sha-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(path, Array.Empty<byte>());
            string actual = await GitHubClient.ComputeSha256Async(path);
            Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeSha256_matches_known_value_for_abc()
    {
        string path = Path.Combine(Path.GetTempPath(), $"zdf-sha-{Guid.NewGuid():N}");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0x61, 0x62, 0x63 });
            string actual = await GitHubClient.ComputeSha256Async(path);
            Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", actual);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SafeExtract_extracts_normal_zip()
    {
        string zipPath = MakeZip(("a.txt", "hello"), ("sub/b.txt", "world"));
        string destDir = Path.Combine(Path.GetTempPath(), $"zdf-zip-{Guid.NewGuid():N}");

        try
        {
            ZipExtractor.SafeExtractToDirectory(zipPath, destDir);
            Assert.Equal("hello", File.ReadAllText(Path.Combine(destDir, "a.txt")));
            Assert.Equal("world", File.ReadAllText(Path.Combine(destDir, "sub", "b.txt")));
        }
        finally
        {
            try { Directory.Delete(destDir, true); } catch { }
            try { File.Delete(zipPath); } catch { }
        }
    }

    [Fact]
    public void SafeExtract_rejects_path_traversal()
    {
        string zipPath = MakeZip(("../escape.txt", "evil"));
        string destDir = Path.Combine(Path.GetTempPath(), $"zdf-zip-{Guid.NewGuid():N}");

        try
        {
            Assert.Throws<InvalidDataException>(() =>
                ZipExtractor.SafeExtractToDirectory(zipPath, destDir));
        }
        finally
        {
            try { Directory.Delete(destDir, true); } catch { }
            try { File.Delete(zipPath); } catch { }
        }
    }

    [Fact]
    public void ExtractMatching_flattens_when_requested()
    {
        string zipPath = MakeZip(
            ("WinDivert-2.2.2-A/x64/WinDivert.dll", "DLL"),
            ("WinDivert-2.2.2-A/x64/WinDivert64.sys", "SYS"),
            ("WinDivert-2.2.2-A/x86/WinDivert.dll", "x86DLL"),
            ("WinDivert-2.2.2-A/LICENSE", "MIT-ish"));
        string destDir = Path.Combine(Path.GetTempPath(), $"zdf-zip-{Guid.NewGuid():N}");

        try
        {
            ZipExtractor.ExtractMatchingTo(zipPath, destDir, BootstrapRunner.IsRelevantWinDivertFile, flatten: true);

            Assert.True(File.Exists(Path.Combine(destDir, "WinDivert.dll")));
            Assert.True(File.Exists(Path.Combine(destDir, "WinDivert64.sys")));
            Assert.True(File.Exists(Path.Combine(destDir, "LICENSE")));
            Assert.Equal("DLL", File.ReadAllText(Path.Combine(destDir, "WinDivert.dll")));
        }
        finally
        {
            try { Directory.Delete(destDir, true); } catch { }
            try { File.Delete(zipPath); } catch { }
        }
    }

    [Fact]
    public void IsRelevantWinwsFile_picks_binaries_and_bin_patterns_only()
    {
        var exe   = new GitHubFileInfo { Name = "winws.exe",   DownloadUrl = "x", Size = 1, Type = "file" };
        var dll   = new GitHubFileInfo { Name = "WinDivert.dll", DownloadUrl = "x", Size = 1, Type = "file" };
        var bin   = new GitHubFileInfo { Name = "quic_initial_www_google_com.bin", DownloadUrl = "x", Size = 1, Type = "file" };
        var md    = new GitHubFileInfo { Name = "readme.md",   DownloadUrl = "x", Size = 1, Type = "file" };
        var rand  = new GitHubFileInfo { Name = "notes.docx",  DownloadUrl = "x", Size = 1, Type = "file" };
        var dir   = new GitHubFileInfo { Name = "subfolder",   DownloadUrl = "",  Size = 0, Type = "dir" };

        Assert.True(BootstrapRunner.IsRelevantWinwsFile(exe));
        Assert.True(BootstrapRunner.IsRelevantWinwsFile(dll));
        Assert.True(BootstrapRunner.IsRelevantWinwsFile(bin));
        Assert.True(BootstrapRunner.IsRelevantWinwsFile(md));
        Assert.False(BootstrapRunner.IsRelevantWinwsFile(rand));
        Assert.False(BootstrapRunner.IsRelevantWinwsFile(dir));
    }

    [Theory]
    [InlineData("WinDivert-2.2.2-A/x64/WinDivert.dll",   true)]
    [InlineData("WinDivert-2.2.2-A/x64/WinDivert64.sys", true)]
    [InlineData("WinDivert-2.2.2-A/x64/WinDivert.lib",   true)]
    [InlineData("WinDivert-2.2.2-A/LICENSE",             true)]
    [InlineData("WinDivert-2.2.2-A/x86/WinDivert.dll",   false)]
    [InlineData("WinDivert-2.2.2-A/include/windivert.h", false)]
    [InlineData("WinDivert-2.2.2-A/README",              false)]
    public void IsRelevantWinDivertFile_keeps_x64_user_mode_and_driver(string path, bool expected)
    {
        Assert.Equal(expected, BootstrapRunner.IsRelevantWinDivertFile(path));
    }

    [Theory]
    [InlineData(WinwsArch.X64,   "zapret-winws")]
    [InlineData(WinwsArch.X86,   "win7")]
    [InlineData(WinwsArch.Arm64, "arm64")]
    public void ResolveWinwsSubpath_maps_known_arches(WinwsArch arch, string expected)
    {
        Assert.Equal(expected, BootstrapRunner.ResolveWinwsSubpath(arch));
    }

    [Fact]
    public void ResolveWinwsSubpath_auto_returns_one_of_known()
    {
        string r = BootstrapRunner.ResolveWinwsSubpath(WinwsArch.Auto);
        Assert.Contains(r, new[] { "zapret-winws", "win7", "arm64" });
    }

    private static string MakeZip(params (string name, string content)[] entries)
    {
        string path = Path.Combine(Path.GetTempPath(), $"zdf-mkzip-{Guid.NewGuid():N}.zip");
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var s = e.Open();
            using var w = new StreamWriter(s);
            w.Write(content);
        }
        return path;
    }
}
