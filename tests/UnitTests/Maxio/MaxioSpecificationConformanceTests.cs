using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Microsoft.eShopWeb.UnitTests.Maxio;

/// <summary>
/// The Maxio OpenAPI specification in maxio-spec/ is the contract this integration is built
/// to, so these tests read the spec itself and fail if the client drifts from it: an operation
/// we call that the spec does not describe, a query parameter the spec does not declare, or a
/// model property that does not exist in the spec's schema.
/// </summary>
public class MaxioSpecificationConformanceTests
{
    private static readonly Lazy<string> SpecDirectory = new(FindSpecDirectory);
    private static readonly Lazy<YamlMappingNode> Specification = new(() => LoadYaml(Path.Combine(SpecDirectory.Value, "openapi.yaml")));

    public static TheoryData<string> OperationIds()
    {
        var data = new TheoryData<string>();
        foreach (var operation in MaxioOperations.All)
        {
            data.Add(operation.OperationId);
        }

        return data;
    }

    public static TheoryData<Type> SpecBackedModels()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(MaxioSchemaAttribute).Assembly.GetTypes()
                     .Where(type => type.GetCustomAttribute<MaxioSchemaAttribute>() is not null))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    public void EveryOperationWeCallExistsInTheSpecification(string operationId)
    {
        var operation = MaxioOperations.All.Single(candidate => candidate.OperationId == operationId);
        var paths = (YamlMappingNode)Specification.Value.Children["paths"];

        var pathEntry = paths.Children.SingleOrDefault(entry => Scalar(entry.Key) == operation.PathTemplate);
        Assert.True(pathEntry.Value is not null, $"The spec does not describe the path {operation.PathTemplate}.");

        var pathItem = (YamlMappingNode)pathEntry.Value!;

        var verb = operation.Method.Method.ToLowerInvariant();
        Assert.True(pathItem.Children.ContainsKey(verb), $"The spec does not describe {verb.ToUpperInvariant()} {operation.PathTemplate}.");

        var specOperation = (YamlMappingNode)pathItem.Children[verb];
        Assert.Equal(operation.OperationId, Scalar(specOperation.Children["operationId"]));

        var declaredQueryParameters = QueryParameterNames(pathItem, specOperation);
        foreach (var parameter in operation.QueryParameters)
        {
            Assert.Contains(parameter, declaredQueryParameters);
        }
    }

    [Theory]
    [MemberData(nameof(SpecBackedModels))]
    public void EveryModelPropertyExistsInTheSpecificationSchema(Type modelType)
    {
        var schemaName = modelType.GetCustomAttribute<MaxioSchemaAttribute>()!.SchemaName;
        var schema = LoadSchema(schemaName);
        var properties = (YamlMappingNode)schema.Children["properties"];
        var declared = properties.Children.Keys.Select(Scalar).ToHashSet(StringComparer.Ordinal);

        foreach (var property in modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            Assert.NotNull(name);
            Assert.True(
                declared.Contains(name!),
                $"{modelType.Name}.{property.Name} maps to '{name}', which the spec schema '{schemaName}' does not declare.");
        }
    }

    [Fact]
    public void RequiredCreateCustomerFieldsAreSent()
    {
        var schema = LoadSchema("Create-Customer");
        var required = ((YamlSequenceNode)schema.Children["required"]).Select(Scalar).ToList();

        var sent = typeof(MaxioCreateCustomer)
            .GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var field in required)
        {
            Assert.Contains(field, sent);
        }
    }

    [Fact]
    public void AuthenticationMatchesTheSpecifiedSecurityScheme()
    {
        var schemes = (YamlMappingNode)((YamlMappingNode)Specification.Value.Children["components"]).Children["securitySchemes"];
        var basic = (YamlMappingNode)schemes.Children["BasicAuth"];

        Assert.Equal("http", Scalar(basic.Children["type"]));
        Assert.Equal("basic", Scalar(basic.Children["scheme"]));

        // The spec's description is what the client implements: API key as user name, "x" as password.
        Assert.Contains("API key", Scalar(basic.Children["description"]), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerUrlTemplatesMatchTheSpecification()
    {
        var servers = (YamlSequenceNode)Specification.Value.Children["servers"];
        var defaultServer = Scalar(((YamlMappingNode)servers[0]).Children["url"]);

        Assert.Equal(defaultServer, MaxioEnvironments.ServerUrlTemplate(MaxioEnvironments.Us));

        var environments = (YamlSequenceNode)((YamlMappingNode)((YamlMappingNode)Specification.Value.Children["info"])
            .Children["x-server-configuration"]).Children["environments"];

        foreach (var environment in environments.Cast<YamlMappingNode>())
        {
            var name = Scalar(environment.Children["name"]);
            var production = ((YamlSequenceNode)environment.Children["servers"])
                .Cast<YamlMappingNode>()
                .Single(server => Scalar(server.Children["name"]) == "production");

            Assert.Equal(Scalar(production.Children["url"]), MaxioEnvironments.ServerUrlTemplate(name));
        }
    }

    [Fact]
    public void TheDefaultPaymentCollectionMethodIsAValueTheSpecificationAllows()
    {
        var allowed = ((YamlSequenceNode)LoadSchema("Collection-Method").Children["enum"]).Select(Scalar).ToList();

        Assert.Contains(new MaxioOptions().PaymentCollectionMethod, allowed);
    }

    [Fact]
    public void SubscriptionStatesWeTreatAsLiveAreStatesTheSpecificationDefines()
    {
        var declared = ((YamlSequenceNode)LoadSchema("Subscription-State").Children["enum"]).Select(Scalar).ToHashSet(StringComparer.Ordinal);

        var live = new[] { "active", "trialing", "pending", "assessing", "paused", "past_due", "soft_failure", "unpaid", "awaiting_signup" };
        var ended = new[] { "canceled", "expired", "failed_to_create", "trial_ended", "on_hold", "suspended" };

        Assert.Equal(declared.OrderBy(state => state), live.Concat(ended).OrderBy(state => state));
        Assert.All(live, state => Assert.True(SubscriptionStates.IsLive(state), $"'{state}' should count as a live subscription."));
        Assert.All(ended, state => Assert.False(SubscriptionStates.IsLive(state), $"'{state}' should not count as a live subscription."));
    }

    private static IReadOnlyCollection<string> QueryParameterNames(YamlMappingNode pathItem, YamlMappingNode operation)
    {
        var names = new List<string>();

        foreach (var source in new[] { pathItem, operation })
        {
            if (!source.Children.TryGetValue("parameters", out var parameters) || parameters is not YamlSequenceNode sequence)
            {
                continue;
            }

            foreach (var parameter in sequence.Cast<YamlMappingNode>())
            {
                if (parameter.Children.TryGetValue("name", out var name) &&
                    parameter.Children.TryGetValue("in", out var location) &&
                    Scalar(location) == "query")
                {
                    names.Add(Scalar(name));
                }
                else if (parameter.Children.TryGetValue("$ref", out var reference))
                {
                    var referenced = LoadNode(Path.Combine(SpecDirectory.Value, Scalar(reference).TrimStart('.', '/')));
                    if (referenced.Children.TryGetValue("in", out var referencedLocation) && Scalar(referencedLocation) == "query")
                    {
                        names.Add(Scalar(referenced.Children["name"]));
                    }
                }
            }
        }

        return names;
    }

    private static YamlMappingNode LoadSchema(string schemaName) =>
        LoadNode(Path.Combine(SpecDirectory.Value, "components", "schemas", schemaName + ".yaml"));

    private static YamlMappingNode LoadNode(string path) => LoadYaml(path);

    private static YamlMappingNode LoadYaml(string path)
    {
        Assert.True(File.Exists(path), $"Expected the Maxio specification file '{path}' to exist.");

        using var reader = new StreamReader(path);
        var stream = new YamlStream();
        stream.Load(reader);

        return (YamlMappingNode)stream.Documents[0].RootNode;
    }

    private static string Scalar(YamlNode node) => ((YamlScalarNode)node).Value!;

    private static string FindSpecDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "maxio-spec");
            if (File.Exists(Path.Combine(candidate, "openapi.yaml")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the maxio-spec folder above the test output directory.");
    }
}
