using System.IO.Compression;

namespace ZDefree.Core.Bootstrap;

public static class ZipExtractor
{
    public static void SafeExtractToDirectory(string zipPath, string destRoot)
    {
        string normalizedRoot = Path.GetFullPath(destRoot);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        Directory.CreateDirectory(destRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            string normalizedName = entry.FullName.Replace('\\', '/').TrimStart('/');
            string target = Path.GetFullPath(Path.Combine(destRoot, normalizedName));

            if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsafe zip entry (path traversal): {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    public static IEnumerable<string> EnumerateFiles(string zipPath, Func<string, bool>? filter = null)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (filter is null || filter(entry.FullName))
            {
                yield return entry.FullName;
            }
        }
    }

    public static void ExtractMatchingTo(string zipPath, string destRoot, Func<string, bool> filter,
        bool flatten = false)
    {
        string normalizedRoot = Path.GetFullPath(destRoot);
        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        Directory.CreateDirectory(destRoot);

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!filter(entry.FullName)) continue;

            string outRel = flatten ? entry.Name : entry.FullName.Replace('\\', '/').TrimStart('/');
            string target = Path.GetFullPath(Path.Combine(destRoot, outRel));

            if (!target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Unsafe zip entry (path traversal): {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
