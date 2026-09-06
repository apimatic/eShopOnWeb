using YamlDotNet.RepresentationModel;

namespace Microsoft.eShopWeb.MaxioBillingTests.Spec;

/// <summary>
/// Reads <c>maxio-spec/openapi.yaml</c> and its external component files.
/// </summary>
/// <remarks>
/// The specification is the contract this integration is built to, so the tests read the real file
/// rather than a fixture. Anything the client sends or reads is checked against it, which turns a
/// provider-side contract change into a failing test instead of a runtime surprise.
/// </remarks>
public sealed class MaxioSpecification
{
    private static readonly Lazy<MaxioSpecification> Shared = new(Load);

    private readonly YamlMappingNode _root;
    private readonly string _specDirectory;
    private readonly Dictionary<string, YamlNode> _fileCache = new(StringComparer.OrdinalIgnoreCase);

    private MaxioSpecification(YamlMappingNode root, string specDirectory)
    {
        _root = root;
        _specDirectory = specDirectory;
    }

    public static MaxioSpecification Instance => Shared.Value;

    /// <summary>Absolute path of the specification folder.</summary>
    public string SpecDirectory => _specDirectory;

    private static MaxioSpecification Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "maxio-spec", "openapi.yaml");
            if (File.Exists(candidate))
            {
                var stream = new YamlStream();
                using var reader = new StreamReader(candidate);
                stream.Load(reader);

                return new MaxioSpecification(
                    (YamlMappingNode)stream.Documents[0].RootNode,
                    Path.GetDirectoryName(candidate)!);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not find maxio-spec/openapi.yaml by walking up from " + AppContext.BaseDirectory);
    }

    /// <summary>
    /// Returns the operation node for a path and HTTP method, or <c>null</c> when the specification
    /// does not describe it.
    /// </summary>
    public YamlMappingNode? FindOperation(string path, string method)
    {
        var paths = (YamlMappingNode)_root.Children[new YamlScalarNode("paths")];

        foreach (var (key, value) in paths.Children)
        {
            if (key is YamlScalarNode scalar
                && string.Equals(scalar.Value, path, StringComparison.Ordinal)
                && value is YamlMappingNode operations
                && operations.Children.TryGetValue(new YamlScalarNode(method.ToLowerInvariant()), out var operation))
            {
                return (YamlMappingNode)operation;
            }
        }

        return null;
    }

    /// <summary>Names of the query parameters the specification declares for an operation.</summary>
    public IReadOnlyCollection<string> QueryParameterNames(string path, string method)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var operation = FindOperation(path, method);
        if (operation is not null)
        {
            CollectParameters(operation, "query", names);
        }

        // Parameters may also be declared once for the whole path item.
        var paths = (YamlMappingNode)_root.Children[new YamlScalarNode("paths")];
        if (paths.Children.TryGetValue(new YamlScalarNode(path), out var pathItem)
            && pathItem is YamlMappingNode pathMapping)
        {
            CollectParameters(pathMapping, "query", names);
        }

        return names;
    }

    /// <summary>Names of the path parameters the specification declares for a path item.</summary>
    public IReadOnlyCollection<string> PathParameterNames(string path)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        var paths = (YamlMappingNode)_root.Children[new YamlScalarNode("paths")];
        if (paths.Children.TryGetValue(new YamlScalarNode(path), out var pathItem)
            && pathItem is YamlMappingNode pathMapping)
        {
            CollectParameters(pathMapping, "path", names);
        }

        return names;
    }

    private void CollectParameters(YamlMappingNode owner, string location, ISet<string> into)
    {
        if (!owner.Children.TryGetValue(new YamlScalarNode("parameters"), out var parameters)
            || parameters is not YamlSequenceNode sequence)
        {
            return;
        }

        foreach (var entry in sequence)
        {
            if (entry is not YamlMappingNode parameter)
            {
                continue;
            }

            var resolved = Resolve(parameter);
            if (resolved is not YamlMappingNode mapping)
            {
                continue;
            }

            if (Scalar(mapping, "in") == location && Scalar(mapping, "name") is { } name)
            {
                into.Add(name);
            }
        }
    }

    /// <summary>
    /// Returns the property names of a schema file under <c>components/schemas</c>, following
    /// <c>allOf</c> composition.
    /// </summary>
    public IReadOnlyCollection<string> SchemaPropertyNames(string relativeSchemaPath)
    {
        var node = LoadFile(relativeSchemaPath);
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectProperties(node, names, relativeSchemaPath);
        return names;
    }

    /// <summary>Returns the values of an <c>enum</c> declared by a schema file.</summary>
    public IReadOnlyList<string> SchemaEnumValues(string relativeSchemaPath)
    {
        var node = LoadFile(relativeSchemaPath);
        if (node is not YamlMappingNode mapping
            || !mapping.Children.TryGetValue(new YamlScalarNode("enum"), out var values)
            || values is not YamlSequenceNode sequence)
        {
            return Array.Empty<string>();
        }

        return sequence.OfType<YamlScalarNode>().Select(scalar => scalar.Value!).ToList();
    }

    /// <summary>Reads a scalar from the root document, e.g. <c>Scalar("openapi")</c>.</summary>
    public string? RootScalar(params string[] keyPath)
    {
        YamlNode current = _root;

        foreach (var key in keyPath)
        {
            if (current is not YamlMappingNode mapping
                || !mapping.Children.TryGetValue(new YamlScalarNode(key), out var next))
            {
                return null;
            }

            current = next;
        }

        return (current as YamlScalarNode)?.Value;
    }

    /// <summary>The first server URL template declared by the specification.</summary>
    public string? FirstServerUrl()
    {
        if (!_root.Children.TryGetValue(new YamlScalarNode("servers"), out var servers)
            || servers is not YamlSequenceNode sequence
            || sequence.FirstOrDefault() is not YamlMappingNode first)
        {
            return null;
        }

        return Scalar(first, "url");
    }

    private void CollectProperties(YamlNode node, ISet<string> into, string origin)
    {
        if (node is not YamlMappingNode mapping)
        {
            return;
        }

        if (mapping.Children.TryGetValue(new YamlScalarNode("properties"), out var properties)
            && properties is YamlMappingNode propertyMap)
        {
            foreach (var key in propertyMap.Children.Keys.OfType<YamlScalarNode>())
            {
                into.Add(key.Value!);
            }
        }

        if (mapping.Children.TryGetValue(new YamlScalarNode("allOf"), out var allOf)
            && allOf is YamlSequenceNode composed)
        {
            foreach (var part in composed)
            {
                CollectProperties(ResolveRelativeTo(part, origin), into, origin);
            }
        }
    }

    /// <summary>Follows a local <c>$ref</c> that points into <c>components/parameters</c>.</summary>
    private YamlNode Resolve(YamlMappingNode node)
    {
        var reference = Scalar(node, "$ref");
        return reference is null ? node : LoadFile(reference);
    }

    private YamlNode ResolveRelativeTo(YamlNode node, string origin)
    {
        if (node is not YamlMappingNode mapping || Scalar(mapping, "$ref") is not { } reference)
        {
            return node;
        }

        // Refs inside a schema file are relative to that file.
        var originDirectory = Path.GetDirectoryName(origin) ?? string.Empty;
        return LoadFile(Path.Combine(originDirectory, reference));
    }

    private YamlNode LoadFile(string relativePath)
    {
        var normalized = relativePath.Replace("./", string.Empty).Replace('\\', '/');
        var full = Path.GetFullPath(Path.Combine(_specDirectory, normalized));

        if (_fileCache.TryGetValue(full, out var cached))
        {
            return cached;
        }

        var stream = new YamlStream();
        using (var reader = new StreamReader(full))
        {
            stream.Load(reader);
        }

        var node = stream.Documents[0].RootNode;
        _fileCache[full] = node;
        return node;
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out var value)
            ? (value as YamlScalarNode)?.Value
            : null;
}
