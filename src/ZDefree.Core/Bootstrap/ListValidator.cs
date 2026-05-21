using System.Text.RegularExpressions;

namespace ZDefree.Core.Bootstrap;

public enum ListKind
{
    Hostlist,
    Ipset,
}

public sealed record ListValidationResult(
    int Accepted,
    int Rejected,
    IReadOnlyList<string> RejectedSamples);

public static class ListValidator
{
    // CIDR form: dotted-quad/prefix OR colon-hex/prefix.
    private static readonly Regex CidrV4 = new(@"^\d{1,3}(\.\d{1,3}){3}/\d{1,2}$", RegexOptions.Compiled);
    private static readonly Regex CidrV6 = new(@"^[0-9a-fA-F:]+/\d{1,3}$",         RegexOptions.Compiled);

    // A bare dotted-quad IPv4 looks like a domain to the parser but is not a hostname.
    private static readonly Regex Ipv4Prefix = new(@"^\d{1,3}(\.\d{1,3}){2,3}",     RegexOptions.Compiled);

    private const int MaxRejectedSamples = 10;

    /// <summary>
    /// Validates and cleans a list of lines. Returns the cleaned, deduplicated, sorted lines.
    /// Hostlist mode: lowercase + sort + dedup; rejects IP-looking lines.
    /// Ipset mode: validates CIDR format; rejects bare IPs and garbage.
    /// </summary>
    public static (IReadOnlyList<string> Cleaned, ListValidationResult Result)
        Validate(IEnumerable<string> lines, ListKind kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = new List<string>();
        var rejectedSamples = new List<string>();
        int rejectedTotal = 0;

        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;

            // Strip trailing inline comments.
            int hash = line.IndexOf('#');
            if (hash > 0) line = line.Substring(0, hash).TrimEnd();
            if (line.Length == 0) continue;

            if (kind == ListKind.Hostlist)
            {
                // Reject lines that look like IPs — those belong in an ipset file.
                if (Ipv4Prefix.IsMatch(line))
                {
                    rejectedTotal++;
                    if (rejectedSamples.Count < MaxRejectedSamples) rejectedSamples.Add(line);
                    continue;
                }
                string lower = line.ToLowerInvariant();
                if (seen.Add(lower))
                {
                    cleaned.Add(lower);
                }
            }
            else
            {
                if (!CidrV4.IsMatch(line) && !CidrV6.IsMatch(line))
                {
                    rejectedTotal++;
                    if (rejectedSamples.Count < MaxRejectedSamples) rejectedSamples.Add(line);
                    continue;
                }
                if (seen.Add(line))
                {
                    cleaned.Add(line);
                }
            }
        }

        cleaned.Sort(StringComparer.Ordinal);

        return (cleaned, new ListValidationResult(
            Accepted: cleaned.Count,
            Rejected: rejectedTotal,
            RejectedSamples: rejectedSamples));
    }
}
