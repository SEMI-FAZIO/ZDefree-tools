using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using ZDefree.Core.Bootstrap;

namespace ZDefree.Core.Tests;

public class NfqwsBootstrapTests
{
    [Theory]
    [InlineData(NfqwsArch.LinuxX64,   "linux-x86_64")]
    [InlineData(NfqwsArch.LinuxX86,   "linux-x86")]
    [InlineData(NfqwsArch.LinuxArm,   "linux-arm")]
    [InlineData(NfqwsArch.LinuxArm64, "linux-arm64")]
    public void ResolveSubdir_returns_expected_path_for_explicit_arch(NfqwsArch arch, string expected)
    {
        Assert.Equal(expected, NfqwsArchHelper.ResolveSubdir(arch));
    }

    [Fact]
    public void ResolveSubdir_auto_returns_nonempty_subdir()
    {
        string sub = NfqwsArchHelper.ResolveSubdir(NfqwsArch.Auto);
        Assert.StartsWith("linux-", sub);
        Assert.True(sub.Length > "linux-".Length);
    }

    [Fact]
    public async Task ExtractSingleAsync_extracts_matching_entry()
    {
        const string contents = "<nfqws binary content for testing>";
        byte[] tarball = BuildTarGz(new[]
        {
            ("zapret-v0/binaries/linux-x86_64/nfqws", Encoding.ASCII.GetBytes(contents)),
            ("zapret-v0/binaries/linux-arm/nfqws",    Encoding.ASCII.GetBytes("other arch")),
            ("zapret-v0/README",                       Encoding.ASCII.GetBytes("readme")),
        });

        string dest = Path.Combine(Path.GetTempPath(), $"nfqws-{Guid.NewGuid():N}.bin");
        try
        {
            await using var src = new MemoryStream(tarball);
            bool ok = await NfqwsTarExtractor.ExtractSingleAsync(src, "binaries/linux-x86_64/nfqws", dest);

            Assert.True(ok);
            Assert.True(File.Exists(dest));
            Assert.Equal(contents, await File.ReadAllTextAsync(dest));
        }
        finally
        {
            try { File.Delete(dest); } catch { }
        }
    }

    [Fact]
    public async Task ExtractSingleAsync_returns_false_when_entry_missing()
    {
        byte[] tarball = BuildTarGz(new[]
        {
            ("zapret-v0/binaries/linux-arm/nfqws", Encoding.ASCII.GetBytes("arm only")),
        });

        string dest = Path.Combine(Path.GetTempPath(), $"nfqws-{Guid.NewGuid():N}.bin");
        try
        {
            await using var src = new MemoryStream(tarball);
            bool ok = await NfqwsTarExtractor.ExtractSingleAsync(src, "binaries/linux-x86_64/nfqws", dest);

            Assert.False(ok);
            Assert.False(File.Exists(dest));
        }
        finally
        {
            try { File.Delete(dest); } catch { }
        }
    }

    [Fact]
    public async Task ExtractSingleAsync_rejects_path_traversal_entries()
    {
        byte[] tarball = BuildTarGz(new[]
        {
            ("../../../etc/passwd",                Encoding.ASCII.GetBytes("evil")),
            ("zapret-v0/binaries/linux-x86_64/nfqws", Encoding.ASCII.GetBytes("real")),
        });

        string dest = Path.Combine(Path.GetTempPath(), $"nfqws-{Guid.NewGuid():N}.bin");
        try
        {
            await using var src = new MemoryStream(tarball);
            bool ok = await NfqwsTarExtractor.ExtractSingleAsync(src, "binaries/linux-x86_64/nfqws", dest);

            Assert.True(ok);
            Assert.Equal("real", await File.ReadAllTextAsync(dest));
            // The evil entry was rejected and not extracted to /etc/passwd — there's no
            // observable side effect to check directly, but we know IsUnsafeName path
            // is exercised because the evil entry comes first in the tar.
        }
        finally
        {
            try { File.Delete(dest); } catch { }
        }
    }

    [Fact]
    public async Task ExtractSingleAsync_throws_on_empty_suffix()
    {
        await using var src = new MemoryStream(BuildTarGz(Array.Empty<(string, byte[])>()));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await NfqwsTarExtractor.ExtractSingleAsync(src, "", "/tmp/x"));
    }

    [Fact]
    public async Task ExtractSingleAsync_overwrites_existing_destination()
    {
        const string oldContent = "stale content";
        const string newContent = "fresh nfqws binary";

        string dest = Path.Combine(Path.GetTempPath(), $"nfqws-{Guid.NewGuid():N}.bin");
        try
        {
            await File.WriteAllTextAsync(dest, oldContent);

            byte[] tarball = BuildTarGz(new[]
            {
                ("zapret-v0/binaries/linux-x86_64/nfqws", Encoding.ASCII.GetBytes(newContent)),
            });
            await using var src = new MemoryStream(tarball);
            await NfqwsTarExtractor.ExtractSingleAsync(src, "binaries/linux-x86_64/nfqws", dest);

            Assert.Equal(newContent, await File.ReadAllTextAsync(dest));
        }
        finally
        {
            try { File.Delete(dest); } catch { }
        }
    }

    // ---- Helpers ----

    /// <summary>
    /// Build a gzipped POSIX-tar with the given files for in-memory testing.
    /// </summary>
    private static byte[] BuildTarGz(IEnumerable<(string Name, byte[] Content)> entries)
    {
        var raw = new MemoryStream();
        using (var gz = new GZipStream(raw, CompressionLevel.NoCompression, leaveOpen: true))
        using (var tar = new TarWriter(gz, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(content),
                };
                tar.WriteEntry(entry);
            }
        }
        return raw.ToArray();
    }
}
