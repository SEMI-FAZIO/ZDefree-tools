using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZDefree.Core.Models;

[JsonConverter(typeof(ListRefConverter))]
public sealed class ListRef
{
    public string? Pack { get; init; }
    public string? Path { get; init; }

    public static ListRef FromPack(string pack) => new() { Pack = pack };

    public bool IsAbsolutePath => Path is not null;
}

internal sealed class ListRefConverter : JsonConverter<ListRef>
{
    public override ListRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ListRef.FromPack(reader.GetString() ?? "");
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected string or object for ListRef, got {reader.TokenType}");
        }

        string? pack = null;
        string? path = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new ListRef { Pack = pack, Path = path };
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected property name in ListRef object");
            }

            string key = reader.GetString() ?? "";
            reader.Read();
            string value = reader.GetString() ?? "";

            switch (key)
            {
                case "pack": pack = value; break;
                case "path": path = value; break;
                default:
                    throw new JsonException($"Unknown ListRef property: {key}");
            }
        }

        throw new JsonException("Unexpected end of ListRef object");
    }

    public override void Write(Utf8JsonWriter writer, ListRef value, JsonSerializerOptions options)
    {
        if (value.Path is null && value.Pack is not null)
        {
            writer.WriteStringValue(value.Pack);
            return;
        }

        writer.WriteStartObject();
        if (value.Pack is not null) writer.WriteString("pack", value.Pack);
        if (value.Path is not null) writer.WriteString("path", value.Path);
        writer.WriteEndObject();
    }
}
