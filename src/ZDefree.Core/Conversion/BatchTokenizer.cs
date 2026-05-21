using System.Text;
using System.Text.RegularExpressions;

namespace ZDefree.Core.Conversion;

public sealed class ParsedBatch
{
    public string? WfTcp { get; init; }
    public string? WfUdp { get; init; }
    public string? WfIface { get; init; }

    public List<List<string>> Rules { get; init; } = new();

    public List<string> Warnings { get; init; } = new();
}

public static class BatchTokenizer
{
    public static ParsedBatch Parse(string batPath)
    {
        byte[] bytes = File.ReadAllBytes(batPath);
        string text = DecodeBytes(bytes);
        return ParseText(text);
    }

    public static ParsedBatch ParseText(string text)
    {
        var warnings = new List<string>();
        string? joined = ExtractWinwsLine(text);
        if (joined is null)
        {
            throw new InvalidDataException("No winws.exe invocation found in the .bat file");
        }

        joined = SubstituteVariables(joined);

        var allTokens = Tokenize(joined);

        string? wfTcp = null, wfUdp = null, wfIface = null;
        var ruleTokens = new List<List<string>>();
        var current = new List<string>();

        foreach (var token in allTokens)
        {
            if (token.Equals("--new", StringComparison.OrdinalIgnoreCase))
            {
                ruleTokens.Add(current);
                current = new List<string>();
                continue;
            }

            if (TryStripPrefix(token, "--wf-tcp=", out var v)) { wfTcp = v; continue; }
            if (TryStripPrefix(token, "--wf-udp=", out v))     { wfUdp = v; continue; }
            if (TryStripPrefix(token, "--wf-iface=", out v))   { wfIface = v; continue; }

            current.Add(token);
        }

        if (current.Count > 0)
        {
            ruleTokens.Add(current);
        }

        return new ParsedBatch
        {
            WfTcp = wfTcp,
            WfUdp = wfUdp,
            WfIface = wfIface,
            Rules = ruleTokens,
            Warnings = warnings,
        };
    }

    internal static string? ExtractWinwsLine(string text)
    {
        var sb = new StringBuilder();
        bool capturing = false;

        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r').TrimEnd();

            if (!capturing)
            {
                int idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                int after = idx + "winws.exe".Length;
                if (after < line.Length && line[after] == '"') after++;
                string tail = line.Substring(after).TrimEnd('^').Trim();
                sb.Append(tail).Append(' ');
                capturing = true;
                if (!line.EndsWith("^")) break;
                continue;
            }

            bool continued = line.EndsWith("^");
            string content = continued ? line.Substring(0, line.Length - 1) : line;
            sb.Append(content.Trim()).Append(' ');
            if (!continued) break;
        }

        if (!capturing) return null;
        return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }

    internal static string SubstituteVariables(string s)
    {
        s = Regex.Replace(s, @"%~dp0bin\\", "bin/", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"%~dp0lists\\", "lists/", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"%BIN%", "bin/", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"%LISTS%", "lists/", RegexOptions.IgnoreCase);

        s = Regex.Replace(s, @"%GameFilterTCP%", "{game_tcp}", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"%GameFilterUDP%", "{game_udp}", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"%GameFilter%",    "{game_all}", RegexOptions.IgnoreCase);

        return s;
    }

    internal static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"')
            {
                inQuote = !inQuote;
                sb.Append(c);
                continue;
            }
            if (c == ' ' && !inQuote)
            {
                if (sb.Length > 0)
                {
                    tokens.Add(StripOuterQuotes(sb.ToString()));
                    sb.Clear();
                }
                continue;
            }
            sb.Append(c);
        }

        if (sb.Length > 0)
        {
            tokens.Add(StripOuterQuotes(sb.ToString()));
        }

        return tokens;
    }

    private static string StripOuterQuotes(string token)
    {
        int eq = token.IndexOf('=');
        if (eq <= 0) return Unquote(token);

        string key = token.Substring(0, eq);
        string val = token.Substring(eq + 1);
        return $"{key}={Unquote(val)}";
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            return s.Substring(1, s.Length - 2);
        }
        return s;
    }

    internal static bool TryStripPrefix(string token, string prefix, out string value)
    {
        if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = token.Substring(prefix.Length);
            return true;
        }
        value = "";
        return false;
    }

    internal static string DecodeBytes(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            return strictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(866).GetString(bytes);
        }
    }
}
