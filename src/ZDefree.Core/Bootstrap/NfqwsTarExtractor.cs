using System.Formats.Tar;
using System.IO.Compression;

namespace ZDefree.Core.Bootstrap;

/// <summary>
/// Extracts a single named entry from a gzipped tar stream. Matches by
/// suffix because bol-van/zapret tarballs prefix everything with a versioned
/// top-level directory (e.g. <c>zapret-v72.12/</c>).
///
/// Hardened against path-traversal: any entry name with <c>..</c> segments
/// or absolute paths is rejected outright.
/// </summary>
public static class NfqwsTarExtractor
{
    /// <summary>
    /// Walks <paramref name="gzippedTar"/>, finds the first entry whose name
    /// ends with <paramref name="entrySuffix"/>, and writes its content to
    /// <paramref name="destPath"/>. Returns true if extracted, false if not found.
    /// </summary>
    public static async Task<bool> ExtractSingleAsync(
        Stream gzippedTar,
        string entrySuffix,
        string destPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(entrySuffix))
        {
            throw new ArgumentException("entrySuffix is required", nameof(entrySuffix));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        await using var gz = new GZipStream(gzippedTar, CompressionMode.Decompress, leaveOpen: false);
        await using var tar = new TarReader(gz, leaveOpen: false);

        while (await tar.GetNextEntryAsync(copyData: false, ct) is { } entry)
        {
            ct.ThrowIfCancellationRequested();

            string name = entry.Name.Replace('\\', '/');
            if (IsUnsafeName(name))                           continue;
            if (entry.EntryType != TarEntryType.RegularFile)  continue;
            if (!name.EndsWith(entrySuffix, StringComparison.Ordinal)) continue;

            // Write to a .part file then atomically rename, matching GitHubClient.DownloadToFileAsync.
            string temp = destPath + ".part";
            await using (var dst = File.Create(temp))
            {
                if (entry.DataStream is not null)
                {
                    await entry.DataStream.CopyToAsync(dst, ct);
                }
            }

            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(temp, destPath);
            return true;
        }

        return false;
    }

    private static bool IsUnsafeName(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (name.StartsWith('/') || name.StartsWith('\\')) return true;
        if (name.Length >= 2 && name[1] == ':') return true;  // C:\...
        foreach (var seg in name.Split('/'))
        {
            if (seg == ".." || seg.Trim() == "..") return true;
        }
        return false;
    }
}
