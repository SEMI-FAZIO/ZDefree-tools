using System.Text.Json.Serialization;

namespace ZDefree.Core.Models;

public sealed class Strategy
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public I18nText? Description { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("tested")]
    public string? Tested { get; set; }

    [JsonPropertyName("compat")]
    public StrategyCompat? Compat { get; set; }

    [JsonPropertyName("filters")]
    public List<FilterGroup> Filters { get; set; } = new();
}

public sealed class StrategyCompat
{
    [JsonPropertyName("tested_isps")]
    public List<string>? TestedIsps { get; set; }

    [JsonPropertyName("min_winws_version")]
    public string? MinWinwsVersion { get; set; }

    [JsonPropertyName("min_zdefree_version")]
    public string? MinZDefreeVersion { get; set; }
}

public sealed class FilterGroup
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("wf")]
    public WfBlock Wf { get; set; } = new();

    [JsonPropertyName("rules")]
    public List<Rule> Rules { get; set; } = new();
}

public sealed class WfBlock
{
    [JsonPropertyName("tcp")]
    public string? Tcp { get; set; }

    [JsonPropertyName("udp")]
    public string? Udp { get; set; }

    [JsonPropertyName("iface")]
    public string? Iface { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }
}

public sealed class Rule
{
    [JsonPropertyName("match")]
    public MatchBlock? Match { get; set; }

    [JsonPropertyName("hostlist")]
    [JsonConverter(typeof(ListRefArrayConverter))]
    public List<ListRef>? Hostlist { get; set; }

    [JsonPropertyName("hostlist_exclude")]
    [JsonConverter(typeof(ListRefArrayConverter))]
    public List<ListRef>? HostlistExclude { get; set; }

    [JsonPropertyName("hostlist_domains")]
    public List<string>? HostlistDomains { get; set; }

    [JsonPropertyName("ipset")]
    [JsonConverter(typeof(ListRefArrayConverter))]
    public List<ListRef>? Ipset { get; set; }

    [JsonPropertyName("ipset_exclude")]
    [JsonConverter(typeof(ListRefArrayConverter))]
    public List<ListRef>? IpsetExclude { get; set; }

    [JsonPropertyName("ip_id")]
    public string? IpId { get; set; }

    [JsonPropertyName("desync")]
    public DesyncCommon? Desync { get; set; }

    [JsonPropertyName("advanced")]
    public DesyncAdvanced? Advanced { get; set; }
}

public sealed class MatchBlock
{
    [JsonPropertyName("tcp")]
    public string? Tcp { get; set; }

    [JsonPropertyName("udp")]
    public string? Udp { get; set; }

    [JsonPropertyName("l3")]
    public string? L3 { get; set; }

    [JsonPropertyName("l7")]
    public List<string>? L7 { get; set; }

    [JsonPropertyName("ssid_filter")]
    public string? SsidFilter { get; set; }
}

public sealed class DesyncCommon
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("ttl")]
    public int? Ttl { get; set; }

    [JsonPropertyName("ttl6")]
    public int? Ttl6 { get; set; }

    [JsonPropertyName("fooling")]
    public List<string>? Fooling { get; set; }

    [JsonPropertyName("repeats")]
    public int? Repeats { get; set; }

    [JsonPropertyName("split_pos")]
    public string? SplitPos { get; set; }

    [JsonPropertyName("split_seqovl")]
    public int? SplitSeqovl { get; set; }

    [JsonPropertyName("split_seqovl_pattern")]
    public string? SplitSeqovlPattern { get; set; }

    [JsonPropertyName("fake_tls")]
    public string? FakeTls { get; set; }

    [JsonPropertyName("fake_quic")]
    public string? FakeQuic { get; set; }

    [JsonPropertyName("fake_unknown_udp")]
    public string? FakeUnknownUdp { get; set; }

    [JsonPropertyName("fake_http")]
    public string? FakeHttp { get; set; }

    [JsonPropertyName("fake_discord")]
    public string? FakeDiscord { get; set; }

    [JsonPropertyName("fake_stun")]
    public string? FakeStun { get; set; }

    [JsonPropertyName("fake_dht")]
    public string? FakeDht { get; set; }

    [JsonPropertyName("fake_wireguard")]
    public string? FakeWireguard { get; set; }

    [JsonPropertyName("any_protocol")]
    public bool? AnyProtocol { get; set; }

    [JsonPropertyName("autottl")]
    public bool? AutoTtl { get; set; }

    [JsonPropertyName("autottl_delta")]
    public string? AutoTtlDelta { get; set; }
}

public sealed class DesyncAdvanced
{
    [JsonPropertyName("wssize")]
    public string? WsSize { get; set; }

    [JsonPropertyName("wscale")]
    public int? WScale { get; set; }

    [JsonPropertyName("orig_mod")]
    public string? OrigMod { get; set; }

    [JsonPropertyName("orig_autottl")]
    public bool? OrigAutoTtl { get; set; }

    [JsonPropertyName("orig_autottl_delta")]
    public string? OrigAutoTtlDelta { get; set; }

    [JsonPropertyName("dup")]
    public int? Dup { get; set; }

    [JsonPropertyName("dup_replace")]
    public string? DupReplace { get; set; }

    [JsonPropertyName("dup_fooling")]
    public List<string>? DupFooling { get; set; }

    [JsonPropertyName("dup_ttl")]
    public int? DupTtl { get; set; }

    [JsonPropertyName("dup_ttl6")]
    public int? DupTtl6 { get; set; }

    [JsonPropertyName("dup_autottl")]
    public bool? DupAutoTtl { get; set; }

    [JsonPropertyName("dup_autottl_delta")]
    public string? DupAutoTtlDelta { get; set; }

    [JsonPropertyName("dup_cutoff")]
    public string? DupCutoff { get; set; }

    [JsonPropertyName("dup_start")]
    public string? DupStart { get; set; }

    [JsonPropertyName("udplen_increment")]
    public int? UdpLenIncrement { get; set; }

    [JsonPropertyName("udplen_pattern")]
    public string? UdpLenPattern { get; set; }

    [JsonPropertyName("ctrack_timeouts")]
    public string? CtrackTimeouts { get; set; }

    [JsonPropertyName("hostlist_auto")]
    public string? HostlistAuto { get; set; }

    [JsonPropertyName("hostlist_auto_fail_threshold")]
    public int? HostlistAutoFailThreshold { get; set; }

    [JsonPropertyName("hostlist_auto_fail_time")]
    public int? HostlistAutoFailTime { get; set; }

    [JsonPropertyName("hostlist_auto_retrans_threshold")]
    public int? HostlistAutoRetransThreshold { get; set; }

    [JsonPropertyName("cutoff")]
    public string? Cutoff { get; set; }

    [JsonPropertyName("start")]
    public string? Start { get; set; }

    [JsonPropertyName("fake_pattern")]
    public string? FakePattern { get; set; }

    [JsonPropertyName("fake_seqofs")]
    public int? FakeSeqOfs { get; set; }

    [JsonPropertyName("fake_pos")]
    public string? FakePos { get; set; }

    [JsonPropertyName("udp_fake_seqlen")]
    public int? UdpFakeSeqLen { get; set; }

    [JsonPropertyName("fake_tls_mod")]
    public string? FakeTlsMod { get; set; }

    [JsonPropertyName("badseq_increment")]
    public int? BadseqIncrement { get; set; }

    [JsonPropertyName("hostfakesplit_mod")]
    public string? HostFakeSplitMod { get; set; }

    [JsonPropertyName("fakedsplit_pattern")]
    public string? FakedSplitPattern { get; set; }

    [JsonPropertyName("raw_args")]
    public List<string>? RawArgs { get; set; }
}
