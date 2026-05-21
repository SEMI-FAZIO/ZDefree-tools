using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZDefree.Core.Probing;

public sealed record IspInfo(
    string Ip,
    string? Country,
    string? Asn,
    string? OrgName,
    string? Raw)
{
    /// <summary>
    /// "AS8402-Vimpelcom"-style tag suitable for matching against
    /// Strategy.Compat.TestedIsps. Prefers ASN over org name.
    /// </summary>
    public string? CompatTag
    {
        get
        {
            if (string.IsNullOrEmpty(Asn)) return null;
            string slug = OrgSlug();
            return string.IsNullOrEmpty(slug) ? Asn : $"{Asn}-{slug}";
        }
    }

    private string OrgSlug()
    {
        if (string.IsNullOrEmpty(OrgName)) return "";
        // First "word" of the org name, alphanumeric only.
        var parts = OrgName.Split(new[] { ' ', '-', '.', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            string cleaned = new string(p.Where(char.IsLetterOrDigit).ToArray());
            if (cleaned.Length >= 3) return cleaned;
        }
        return "";
    }
}

public interface IIspDetector
{
    Task<IspInfo> DetectAsync(CancellationToken ct = default);
}

public sealed class IspDetector : IIspDetector, IDisposable
{
    // Default endpoint: ipinfo.io free tier (HTTPS, no auth required, ~50K/month).
    // Response shape: { ip, city, region, country, loc, org: "AS<num> <name>" }.
    public const string DefaultEndpoint = "https://ipinfo.io/json";

    private static readonly Regex AsnInOrg = new(@"^\s*AS(\d+)\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _endpoint;

    public IspDetector(HttpClient? http = null, string? endpoint = null)
    {
        if (http is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            _ownsClient = true;
        }
        else
        {
            _http = http;
            _ownsClient = false;
        }

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("ZDefree-IspDetector/0.1");
        }

        _endpoint = endpoint ?? DefaultEndpoint;
    }

    public async Task<IspInfo> DetectAsync(CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(_endpoint, ct);
        resp.EnsureSuccessStatusCode();
        string body = await resp.Content.ReadAsStringAsync(ct);
        return ParseIpinfoResponse(body);
    }

    /// <summary>
    /// Public for testability — parses the ipinfo.io /json response format.
    /// </summary>
    public static IspInfo ParseIpinfoResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string ip      = root.TryGetProperty("ip",      out var ipEl)      ? ipEl.GetString() ?? "" : "";
        string? country= root.TryGetProperty("country", out var countryEl) ? countryEl.GetString() : null;
        string? org    = root.TryGetProperty("org",     out var orgEl)     ? orgEl.GetString() : null;

        string? asn = null;
        string? orgName = org;
        if (!string.IsNullOrEmpty(org))
        {
            var m = AsnInOrg.Match(org);
            if (m.Success)
            {
                asn     = "AS" + m.Groups[1].Value;
                orgName = m.Groups[2].Value.Trim();
            }
        }

        return new IspInfo(ip, country, asn, orgName, org);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
