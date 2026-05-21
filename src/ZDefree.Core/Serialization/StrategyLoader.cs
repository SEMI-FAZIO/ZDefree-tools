using System.Text.Json;
using System.Text.Json.Serialization;
using ZDefree.Core.Models;

namespace ZDefree.Core.Serialization;

public static class StrategyLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static Strategy LoadFromFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Strategy file not found: {path}");
        }

        string json = File.ReadAllText(path);
        return LoadFromString(json, sourcePath: path);
    }

    public static Strategy LoadFromString(string json, string? sourcePath = null)
    {
        try
        {
            var strategy = JsonSerializer.Deserialize<Strategy>(json, Options)
                ?? throw new InvalidDataException($"Strategy parsed to null{(sourcePath is null ? "" : $" in {sourcePath}")}");

            Validate(strategy, sourcePath);
            return strategy;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Invalid strategy JSON{(sourcePath is null ? "" : $" in {sourcePath}")}: {ex.Message}", ex);
        }
    }

    public static string Serialize(Strategy strategy)
    {
        return JsonSerializer.Serialize(strategy, Options);
    }

    private static void Validate(Strategy s, string? sourcePath)
    {
        string ctx = sourcePath is null ? "" : $" ({sourcePath})";

        if (string.IsNullOrEmpty(s.Id))
        {
            throw new InvalidDataException($"Strategy 'id' is required{ctx}");
        }

        if (string.IsNullOrEmpty(s.Name))
        {
            throw new InvalidDataException($"Strategy 'name' is required{ctx}");
        }

        if (s.Filters.Count == 0)
        {
            throw new InvalidDataException($"Strategy '{s.Id}' must have at least one filter group{ctx}");
        }

        for (int i = 0; i < s.Filters.Count; i++)
        {
            var f = s.Filters[i];
            if (f.Rules.Count == 0)
            {
                throw new InvalidDataException(
                    $"Strategy '{s.Id}' filter[{i}] must have at least one rule{ctx}");
            }

            if (string.IsNullOrEmpty(f.Wf.Tcp) && string.IsNullOrEmpty(f.Wf.Udp) && string.IsNullOrEmpty(f.Wf.Raw))
            {
                throw new InvalidDataException(
                    $"Strategy '{s.Id}' filter[{i}].wf must specify tcp, udp, or raw{ctx}");
            }
        }
    }
}
