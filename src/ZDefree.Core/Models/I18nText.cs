using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZDefree.Core.Models;

[JsonConverter(typeof(I18nTextConverter))]
public sealed class I18nText
{
    public string? En { get; init; }
    public string? Ru { get; init; }
    public Dictionary<string, string>? Other { get; init; }

    public static I18nText FromString(string s) => new() { En = s };

    public string Resolve(string lang)
    {
        if (lang.Equals("ru", StringComparison.OrdinalIgnoreCase) && Ru is not null) return Ru;
        if (lang.Equals("en", StringComparison.OrdinalIgnoreCase) && En is not null) return En;
        if (Other is not null && Other.TryGetValue(lang, out var v)) return v;
        return En ?? Ru ?? Other?.Values.FirstOrDefault() ?? "";
    }
}

internal sealed class I18nTextConverter : JsonConverter<I18nText>
{
    public override I18nText Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return I18nText.FromString(reader.GetString() ?? "");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected string or object for I18nText, got {reader.TokenType}");
        }

        string? en = null;
        string? ru = null;
        Dictionary<string, string>? other = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new I18nText { En = en, Ru = ru, Other = other };
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in I18nText object");
            }

            string key = reader.GetString() ?? "";
            reader.Read();
            string value = reader.GetString() ?? "";

            switch (key)
            {
                case "en": en = value; break;
                case "ru": ru = value; break;
                default:
                    other ??= new();
                    other[key] = value;
                    break;
            }
        }

        throw new JsonException("Unexpected end of I18nText object");
    }

    public override void Write(Utf8JsonWriter writer, I18nText value, JsonSerializerOptions options)
    {
        if (value.Ru is null && value.Other is null && value.En is not null)
        {
            writer.WriteStringValue(value.En);
            return;
        }

        writer.WriteStartObject();
        if (value.En is not null) writer.WriteString("en", value.En);
        if (value.Ru is not null) writer.WriteString("ru", value.Ru);
        if (value.Other is not null)
        {
            foreach (var kvp in value.Other)
            {
                writer.WriteString(kvp.Key, kvp.Value);
            }
        }
        writer.WriteEndObject();
    }
}
