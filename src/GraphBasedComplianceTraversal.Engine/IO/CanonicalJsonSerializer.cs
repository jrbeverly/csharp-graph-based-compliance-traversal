using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GraphBasedComplianceTraversal.Engine.IO;

public static class CanonicalJsonSerializer
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static byte[] Serialize(RepositoryData repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        return Serialize(new JsonObject
        {
            ["records"] = ToJson(repository.Records),
            ["fixtures"] = ToJson(repository.Fixtures),
        });
    }

    public static byte[] Serialize(JsonNode value)
    {
        ArgumentNullException.ThrowIfNull(value);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            WriteNode(writer, value);
        }

        return stream.ToArray();
    }

    public static string SerializeToString(JsonNode value) => Encoding.UTF8.GetString(Serialize(value));

    private static JsonArray ToJson(IEnumerable<RepositoryDocument> documents) =>
        new(documents.Select(document => (JsonNode)new JsonObject
        {
            ["id"] = document.Identifier,
            ["path"] = document.RelativePath,
            ["format"] = document.Format.ToString().ToLowerInvariant(),
            ["content"] = document.Content.DeepClone(),
        }).ToArray());

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject jsonObject:
                writer.WriteStartObject();
                foreach (var property in jsonObject.OrderBy(property => property.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Key);
                    WriteNode(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray jsonArray:
                writer.WriteStartArray();
                foreach (var item in OrderCollection(jsonArray))
                {
                    WriteNode(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValue jsonValue:
                jsonValue.WriteTo(writer);
                break;
            default:
                throw new JsonException($"Unsupported JSON node type '{node.GetType().Name}'.");
        }
    }

    private static IEnumerable<JsonNode?> OrderCollection(JsonArray array)
    {
        if (array.Count > 1 && array.All(item => TryGetIdentifier(item, out _)))
        {
            return array.OrderBy(item => GetIdentifier(item), StringComparer.Ordinal).ToArray();
        }

        return array;
    }

    private static bool TryGetIdentifier(JsonNode? node, out string identifier)
    {
        identifier = string.Empty;
        if (node is not JsonObject jsonObject || jsonObject["id"] is not JsonValue value)
        {
            return false;
        }

        identifier = value.ToString();
        return true;
    }

    private static string GetIdentifier(JsonNode? node)
    {
        _ = TryGetIdentifier(node, out var identifier);
        return identifier;
    }
}
