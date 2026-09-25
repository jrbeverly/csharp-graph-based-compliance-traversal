using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace GraphBasedComplianceTraversal.Engine.IO;

public static class RepositoryDataLoader
{
    public static RepositoryData Load(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(root))
        {
            throw new RepositoryDataException(root, "repository root does not exist.");
        }

        var records = LoadDirectory(root, "data", ["*.yaml", "*.yml"], RepositoryDocumentFormat.Yaml);
        var fixtures = LoadDirectory(root, "fixtures", ["*.json"], RepositoryDocumentFormat.Json);
        return new RepositoryData(records, fixtures);
    }

    private static RepositoryDocument[] LoadDirectory(
        string root,
        string relativeDirectory,
        IReadOnlyList<string> patterns,
        RepositoryDocumentFormat format)
    {
        var directory = Path.Combine(root, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            throw new RepositoryDataException(relativeDirectory, "required directory does not exist.");
        }

        try
        {
            return patterns
                .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories))
                .Select(path => LoadDocument(root, path, format))
                .OrderBy(document => document.Identifier is null)
                .ThenBy(document => document.Identifier, StringComparer.Ordinal)
                .ThenBy(document => document.RelativePath, StringComparer.Ordinal)
                .ToArray();
        }
        catch (RepositoryDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new RepositoryDataException(relativeDirectory, exception.Message, exception);
        }
    }

    private static RepositoryDocument LoadDocument(
        string root,
        string path,
        RepositoryDocumentFormat format)
    {
        var relativePath = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

        try
        {
            var text = File.ReadAllText(path);
            var content = format == RepositoryDocumentFormat.Json ? ParseJson(text) : ParseYaml(text);
            var identifierNode = (content as JsonObject)?["id"];
            var identifier = identifierNode is null
                ? null
                : identifierNode.GetValueKind() == JsonValueKind.String
                    ? identifierNode.GetValue<string>()
                    : identifierNode.ToJsonString();
            return new RepositoryDocument(relativePath, format, identifier, content);
        }
        catch (RepositoryDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or YamlException
            or InvalidOperationException or FormatException)
        {
            throw new RepositoryDataException(relativePath, exception.Message, exception);
        }
    }

    private static JsonNode ParseJson(string text) =>
        JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        }) ?? throw new JsonException("The document is empty.");

    private static JsonNode ParseYaml(string text)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(text);
        stream.Load(reader);

        if (stream.Documents.Count != 1)
        {
            throw new YamlException("Expected exactly one YAML document.");
        }

        return ConvertYamlNode(stream.Documents[0].RootNode)
            ?? throw new YamlException("The document root cannot be null.");
    }

    private static JsonNode? ConvertYamlNode(YamlNode node) => node switch
    {
        YamlMappingNode mapping => ConvertMapping(mapping),
        YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(ConvertYamlNode).ToArray()),
        YamlScalarNode scalar => ConvertScalar(scalar),
        _ => throw new YamlException($"Unsupported YAML node type '{node.NodeType}'."),
    };

    private static JsonObject ConvertMapping(YamlMappingNode mapping)
    {
        var result = new JsonObject();
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is not YamlScalarNode { Value: not null } key)
            {
                throw new YamlException("Mapping keys must be non-null strings.");
            }

            if (result.ContainsKey(key.Value))
            {
                throw new YamlException($"Duplicate mapping key '{key.Value}'.");
            }

            result.Add(key.Value, ConvertYamlNode(pair.Value));
        }

        return result;
    }

    private static JsonValue? ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value;
        if (value is null || IsPlainScalar(scalar) && value is "null" or "Null" or "NULL" or "~")
        {
            return null;
        }

        if (IsPlainScalar(scalar) && bool.TryParse(value, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        if (IsPlainScalar(scalar)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (IsPlainScalar(scalar)
            && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static bool IsPlainScalar(YamlScalarNode scalar) => scalar.Style == ScalarStyle.Plain;
}
