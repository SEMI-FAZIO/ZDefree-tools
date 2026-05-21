using ZDefree.Core.Bootstrap;

namespace ZDefree.Core.Tests;

public class ListValidatorTests
{
    [Fact]
    public void Hostlist_rejects_lines_starting_with_ipv4_pattern()
    {
        var input = new[] { "discord.com", "1.2.3.4", "192.168.1.1", "example.org" };
        var (cleaned, result) = ListValidator.Validate(input, ListKind.Hostlist);

        Assert.Equal(2, cleaned.Count);
        Assert.DoesNotContain("1.2.3.4", cleaned);
        Assert.DoesNotContain("192.168.1.1", cleaned);
        Assert.Equal(2, result.Rejected);
    }

    [Fact]
    public void Hostlist_strips_comments_and_blanks()
    {
        var input = new[] { "# header comment", "", "  ", "discord.com", "// inline-style comment", "youtube.com   # trailing" };
        var (cleaned, _) = ListValidator.Validate(input, ListKind.Hostlist);

        Assert.Contains("discord.com", cleaned);
        Assert.Contains("youtube.com", cleaned);
        Assert.DoesNotContain(cleaned, l => l.Contains("comment"));
    }

    [Fact]
    public void Hostlist_deduplicates_case_insensitively_and_lowercases()
    {
        var input = new[] { "Discord.com", "DISCORD.COM", "discord.com" };
        var (cleaned, _) = ListValidator.Validate(input, ListKind.Hostlist);

        Assert.Single(cleaned);
        Assert.Equal("discord.com", cleaned[0]);
    }

    [Fact]
    public void Hostlist_output_is_sorted_lexicographically()
    {
        var input = new[] { "zeta.com", "alpha.com", "mu.net" };
        var (cleaned, _) = ListValidator.Validate(input, ListKind.Hostlist);

        Assert.Equal(new[] { "alpha.com", "mu.net", "zeta.com" }, cleaned);
    }

    [Fact]
    public void Ipset_accepts_ipv4_cidr_and_ipv6_cidr()
    {
        var input = new[] { "138.128.140.253/32", "10.0.0.0/8", "2001:db8::/32" };
        var (cleaned, result) = ListValidator.Validate(input, ListKind.Ipset);

        Assert.Equal(3, cleaned.Count);
        Assert.Equal(0, result.Rejected);
    }

    [Fact]
    public void Ipset_rejects_bare_ip_and_garbage()
    {
        var input = new[] { "138.128.140.253", "not-an-ip", "10.0.0.0/8" };
        var (cleaned, result) = ListValidator.Validate(input, ListKind.Ipset);

        Assert.Single(cleaned);
        Assert.Equal("10.0.0.0/8", cleaned[0]);
        Assert.Equal(2, result.Rejected);
    }

    [Fact]
    public void Ipset_deduplicates_exact_duplicates()
    {
        var input = new[] { "10.0.0.0/8", "10.0.0.0/8", "10.0.0.0/8" };
        var (cleaned, _) = ListValidator.Validate(input, ListKind.Ipset);

        Assert.Single(cleaned);
    }

    [Fact]
    public void Rejected_samples_are_capped()
    {
        var input = Enumerable.Range(0, 100).Select(i => $"1.2.3.{i}").ToList();
        var (_, result) = ListValidator.Validate(input, ListKind.Hostlist);

        Assert.Equal(100, result.Rejected);
        Assert.True(result.RejectedSamples.Count <= 10);
    }
}
