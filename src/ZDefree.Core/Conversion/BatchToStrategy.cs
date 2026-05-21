using ZDefree.Core.Models;

namespace ZDefree.Core.Conversion;

public sealed class ConversionResult
{
    public required Strategy Strategy { get; init; }
    public List<string> Warnings { get; init; } = new();
}

public static class BatchToStrategy
{
    public static ConversionResult Convert(string batPath, string? idOverride = null, string? nameOverride = null)
    {
        var parsed = BatchTokenizer.Parse(batPath);
        return Convert(parsed,
            idOverride ?? DeriveId(batPath),
            nameOverride ?? DeriveName(batPath));
    }

    public static ConversionResult Convert(ParsedBatch parsed, string id, string name)
    {
        var warnings = new List<string>(parsed.Warnings);

        var wf = new WfBlock
        {
            Tcp = parsed.WfTcp,
            Udp = parsed.WfUdp,
            Iface = parsed.WfIface,
        };

        var rules = parsed.Rules.Select(r => BuildRule(r, warnings)).ToList();

        if (rules.Count == 0)
        {
            throw new InvalidDataException("Parsed batch produced zero rules");
        }

        var strategy = new Strategy
        {
            Id = id,
            Name = name,
            Version = 1,
            Filters =
            {
                new FilterGroup { Name = "main", Wf = wf, Rules = rules }
            }
        };

        return new ConversionResult { Strategy = strategy, Warnings = warnings };
    }

    private static Rule BuildRule(List<string> tokens, List<string> warnings)
    {
        var match = new MatchBlock();
        var desync = new DesyncCommon();
        var advanced = new DesyncAdvanced();
        bool matchUsed = false, desyncUsed = false, advancedUsed = false;

        List<ListRef>? hostlist = null;
        List<ListRef>? hostlistExclude = null;
        List<ListRef>? ipset = null;
        List<ListRef>? ipsetExclude = null;
        List<string>? hostlistDomains = null;
        string? ipId = null;

        foreach (var token in tokens)
        {
            string key;
            string? value;
            int eq = token.IndexOf('=');
            if (eq < 0)
            {
                key = token;
                value = null;
            }
            else
            {
                key = token.Substring(0, eq);
                value = token.Substring(eq + 1);
            }

            switch (key)
            {
                case "--filter-tcp":      match.Tcp = value; matchUsed = true; break;
                case "--filter-udp":      match.Udp = value; matchUsed = true; break;
                case "--filter-l3":       match.L3 = value;  matchUsed = true; break;
                case "--filter-l7":
                    match.L7 = value?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    matchUsed = true; break;
                case "--ssid-filter":     match.SsidFilter = value; matchUsed = true; break;

                case "--hostlist":
                    AppendListRef(ref hostlist, value, "list-", ".txt"); break;
                case "--hostlist-exclude":
                    AppendListRef(ref hostlistExclude, value, "list-", ".txt"); break;
                case "--hostlist-domains":
                    hostlistDomains = value?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    break;
                case "--ipset":
                    AppendListRef(ref ipset, value, "ipset-", ".txt"); break;
                case "--ipset-exclude":
                    AppendListRef(ref ipsetExclude, value, "ipset-", ".txt"); break;
                case "--ip-id":               ipId = value; break;

                case "--dpi-desync":              desync.Mode = value; desyncUsed = true; break;
                case "--dpi-desync-ttl":          desync.Ttl = ParseInt(value); desyncUsed = true; break;
                case "--dpi-desync-ttl6":         desync.Ttl6 = ParseInt(value); desyncUsed = true; break;
                case "--dpi-desync-fooling":
                    desync.Fooling = value?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    desyncUsed = true; break;
                case "--dpi-desync-repeats":      desync.Repeats = ParseInt(value); desyncUsed = true; break;
                case "--dpi-desync-split-pos":    desync.SplitPos = value; desyncUsed = true; break;
                case "--dpi-desync-split-seqovl": desync.SplitSeqovl = ParseInt(value); desyncUsed = true; break;
                case "--dpi-desync-split-seqovl-pattern":
                    desync.SplitSeqovlPattern = value; desyncUsed = true; break;

                case "--dpi-desync-fake-tls":         desync.FakeTls = value; desyncUsed = true; break;
                case "--dpi-desync-fake-quic":        desync.FakeQuic = value; desyncUsed = true; break;
                case "--dpi-desync-fake-unknown-udp": desync.FakeUnknownUdp = value; desyncUsed = true; break;
                case "--dpi-desync-fake-http":        desync.FakeHttp = value; desyncUsed = true; break;
                case "--dpi-desync-fake-discord":     desync.FakeDiscord = value; desyncUsed = true; break;
                case "--dpi-desync-fake-stun":        desync.FakeStun = value; desyncUsed = true; break;
                case "--dpi-desync-fake-dht":         desync.FakeDht = value; desyncUsed = true; break;
                case "--dpi-desync-fake-wireguard":   desync.FakeWireguard = value; desyncUsed = true; break;

                case "--dpi-desync-any-protocol":
                    desync.AnyProtocol = value == "1" || value is null || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    desyncUsed = true; break;
                case "--dpi-desync-autottl":
                    if (string.IsNullOrEmpty(value)) desync.AutoTtl = true;
                    else                              desync.AutoTtlDelta = value;
                    desyncUsed = true; break;

                case "--wssize":          advanced.WsSize = value; advancedUsed = true; break;
                case "--wscale":          advanced.WScale = ParseInt(value); advancedUsed = true; break;
                case "--orig-mod":        advanced.OrigMod = value; advancedUsed = true; break;
                case "--orig-autottl":
                    if (string.IsNullOrEmpty(value)) advanced.OrigAutoTtl = true;
                    else                              advanced.OrigAutoTtlDelta = value;
                    advancedUsed = true; break;

                case "--dup":             advanced.Dup = ParseInt(value); advancedUsed = true; break;
                case "--dup-replace":     advanced.DupReplace = value; advancedUsed = true; break;
                case "--dup-fooling":
                    advanced.DupFooling = value?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
                    advancedUsed = true; break;
                case "--dup-ttl":         advanced.DupTtl = ParseInt(value); advancedUsed = true; break;
                case "--dup-ttl6":        advanced.DupTtl6 = ParseInt(value); advancedUsed = true; break;
                case "--dup-autottl":
                    if (string.IsNullOrEmpty(value)) advanced.DupAutoTtl = true;
                    else                              advanced.DupAutoTtlDelta = value;
                    advancedUsed = true; break;
                case "--dup-cutoff":      advanced.DupCutoff = value; advancedUsed = true; break;
                case "--dup-start":       advanced.DupStart = value; advancedUsed = true; break;

                case "--udplen-increment": advanced.UdpLenIncrement = ParseInt(value); advancedUsed = true; break;
                case "--udplen-pattern":   advanced.UdpLenPattern = value; advancedUsed = true; break;

                case "--ctrack-timeouts":  advanced.CtrackTimeouts = value; advancedUsed = true; break;
                case "--hostlist-auto":    advanced.HostlistAuto = value; advancedUsed = true; break;
                case "--hostlist-auto-fail-threshold":
                    advanced.HostlistAutoFailThreshold = ParseInt(value); advancedUsed = true; break;
                case "--hostlist-auto-fail-time":
                    advanced.HostlistAutoFailTime = ParseInt(value); advancedUsed = true; break;
                case "--hostlist-auto-retrans-threshold":
                    advanced.HostlistAutoRetransThreshold = ParseInt(value); advancedUsed = true; break;

                case "--dpi-desync-cutoff":         advanced.Cutoff = value; advancedUsed = true; break;
                case "--dpi-desync-start":          advanced.Start = value; advancedUsed = true; break;
                case "--dpi-desync-fake-pattern":   advanced.FakePattern = value; advancedUsed = true; break;
                case "--dpi-desync-fake-seqofs":    advanced.FakeSeqOfs = ParseInt(value); advancedUsed = true; break;
                case "--dpi-desync-fake-pos":       advanced.FakePos = value; advancedUsed = true; break;
                case "--dpi-desync-udp-fake-seqlen":advanced.UdpFakeSeqLen = ParseInt(value); advancedUsed = true; break;

                default:
                    advanced.RawArgs ??= new();
                    advanced.RawArgs.Add(token);
                    advancedUsed = true;
                    warnings.Add($"Unknown flag passed through to advanced.raw_args: {key}");
                    break;
            }
        }

        return new Rule
        {
            Match = matchUsed ? match : null,
            Hostlist = hostlist,
            HostlistExclude = hostlistExclude,
            HostlistDomains = hostlistDomains,
            Ipset = ipset,
            IpsetExclude = ipsetExclude,
            IpId = ipId,
            Desync = desyncUsed ? desync : null,
            Advanced = advancedUsed ? advanced : null,
        };
    }

    private static ListRef? MakeListRef(string? raw, string filePrefix, string fileSuffix)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        string normalized = raw.Replace('\\', '/');
        if (normalized.StartsWith("lists/", StringComparison.OrdinalIgnoreCase))
        {
            string fileName = normalized.Substring("lists/".Length);
            string baseName = fileName;
            if (baseName.StartsWith(filePrefix, StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(filePrefix.Length);
            if (baseName.EndsWith(fileSuffix, StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - fileSuffix.Length);

            return new ListRef { Pack = baseName };
        }

        return new ListRef { Path = raw };
    }

    private static void AppendListRef(ref List<ListRef>? target, string? raw, string filePrefix, string fileSuffix)
    {
        var item = MakeListRef(raw, filePrefix, fileSuffix);
        if (item is null) return;
        target ??= new();
        target.Add(item);
    }

    private static int? ParseInt(string? s)
        => int.TryParse(s, out var n) ? n : null;

    private static string DeriveId(string batPath)
    {
        string name = Path.GetFileNameWithoutExtension(batPath).ToLowerInvariant();
        return new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-')
            .Replace("--", "-");
    }

    private static string DeriveName(string batPath)
    {
        return Path.GetFileNameWithoutExtension(batPath);
    }
}
