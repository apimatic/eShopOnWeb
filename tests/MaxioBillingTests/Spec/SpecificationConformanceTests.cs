using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;
using Xunit;

namespace Microsoft.eShopWeb.MaxioBillingTests.Spec;

/// <summary>
/// Checks this integration against <c>maxio-spec/openapi.yaml</c>.
/// </summary>
/// <remarks>
/// The specification is the contract, so these tests exist to make drift loud. Every operation the
/// client calls, every query and path parameter it fills in, and every property of every model it
/// exchanges is looked up in the specification. Nothing is asserted from memory.
/// </remarks>
public class SpecificationConformanceTests
{
    private static readonly MaxioSpecification Specification = MaxioSpecification.Instance;

    /// <summary>
    /// Every operation <c>IMaxioApiClient</c> exposes, as path, method and specification operation
    /// id. Keep this table in step with the client.
    /// </summary>
    public static TheoryData<string, string, string> Operations => new()
    {
        { "/site.json", "get", "readSite" },
        { "/product_families/{product_family_id}/products.json", "get", "listProductsForProductFamily" },
        { "/customers.json", "post", "createCustomer" },
        { "/customers/lookup.json", "get", "readCustomerByReference" },
        { "/customers/{customer_id}/subscriptions.json", "get", "listCustomerSubscriptions" },
        { "/subscriptions.json", "post", "createSubscription" },
        { "/subscriptions/lookup.json", "get", "findSubscription" }
    };

    [Theory]
    [MemberData(nameof(Operations))]
    public void Every_called_operation_exists_in_the_specification(string path, string method, string operationId)
    {
        var operation = Specification.FindOperation(path, method);

        Assert.True(operation is not null, $"The specification declares no {method.ToUpperInvariant()} {path}.");
        Assert.Equal(operationId, ReadScalar(operation!, "operationId"));
    }

    [Fact]
    public void Product_family_products_is_addressed_by_its_declared_path_parameter()
    {
        var names = Specification.PathParameterNames("/product_families/{product_family_id}/products.json");

        Assert.Contains("product_family_id", names);
    }

    [Fact]
    public void Customer_subscriptions_is_addressed_by_its_declared_path_parameter()
    {
        var names = Specification.PathParameterNames("/customers/{customer_id}/subscriptions.json");

        Assert.Contains("customer_id", names);
    }

    [Theory]
    [InlineData("/product_families/{product_family_id}/products.json", "get", "page")]
    [InlineData("/product_families/{product_family_id}/products.json", "get", "per_page")]
    [InlineData("/customers/lookup.json", "get", "reference")]
    [InlineData("/subscriptions/lookup.json", "get", "reference")]
    public void Every_query_parameter_sent_is_declared_by_the_specification(string path, string method, string parameter)
    {
        var names = Specification.QueryParameterNames(path, method);

        Assert.Contains(parameter, names);
    }

    [Fact]
    public void Authentication_follows_the_specification_security_scheme()
    {
        Assert.Equal("http", Specification.RootScalar("components", "securitySchemes", "BasicAuth", "type"));
        Assert.Equal("basic", Specification.RootScalar("components", "securitySchemes", "BasicAuth", "scheme"));
    }

    [Fact]
    public void Derived_base_address_matches_the_specification_server_template()
    {
        var template = Specification.FirstServerUrl();
        Assert.Equal("https://{site}.chargify.com", template);

        var options = new MaxioOptions { ApiKey = "k", Subdomain = "acme", ProductFamilyHandle = "family" };

        Assert.Equal(
            template!.Replace("{site}", "acme") + "/",
            options.ResolveBaseAddress().ToString());
    }

    [Fact]
    public void Supported_collection_methods_match_the_specification_enumeration()
    {
        var declared = Specification.SchemaEnumValues("components/schemas/Collection-Method.yaml");

        Assert.Equal(
            declared.OrderBy(value => value, StringComparer.Ordinal),
            MaxioOptions.SupportedCollectionMethods.OrderBy(value => value, StringComparer.Ordinal));
    }

    [Fact]
    public void Subscription_states_cover_the_specification_enumeration()
    {
        var declared = Specification.SchemaEnumValues("components/schemas/Subscription-State.yaml");
        Assert.NotEmpty(declared);

        var mapped = Enum.GetNames<SubscriptionState>()
            .Where(name => name != nameof(SubscriptionState.Unknown))
            .Select(ToSnakeCase)
            .OrderBy(value => value, StringComparer.Ordinal);

        Assert.Equal(declared.OrderBy(value => value, StringComparer.Ordinal), mapped);
    }

    /// <summary>
    /// Every transcribed model property must name a property that the corresponding specification
    /// schema actually declares.
    /// </summary>
    public static TheoryData<Type, string> Models => new()
    {
        { typeof(MaxioCustomer), "components/schemas/Customer.yaml" },
        { typeof(MaxioCustomerResponse), "components/schemas/Customer-Response.yaml" },
        { typeof(MaxioCreateCustomer), "components/schemas/Create-Customer.yaml" },
        { typeof(MaxioCreateCustomerRequest), "components/schemas/Create-Customer-Request.yaml" },
        { typeof(MaxioProduct), "components/schemas/Product.yaml" },
        { typeof(MaxioProductResponse), "components/schemas/Product-Response.yaml" },
        { typeof(MaxioProductFamily), "components/schemas/Product-Family.yaml" },
        { typeof(MaxioSubscription), "components/schemas/Subscription.yaml" },
        { typeof(MaxioSubscriptionResponse), "components/schemas/Subscription-Response.yaml" },
        { typeof(MaxioCreateSubscription), "components/schemas/Create-Subscription.yaml" },
        { typeof(MaxioCreateSubscriptionRequest), "components/schemas/Create-Subscription-Request.yaml" },
        { typeof(MaxioSite), "components/schemas/Site.yaml" },
        { typeof(MaxioSiteResponse), "components/schemas/Site-Response.yaml" }
    };

    [Theory]
    [MemberData(nameof(Models))]
    public void Every_model_property_exists_in_the_specification_schema(Type model, string schemaPath)
    {
        var declared = Specification.SchemaPropertyNames(schemaPath);
        Assert.NotEmpty(declared);

        var undeclared = model
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                                ?? throw new InvalidOperationException(
                                    $"{model.Name}.{property.Name} has no [JsonPropertyName]; the wire name must be explicit."))
            .Where(name => !declared.Contains(name))
            .ToList();

        Assert.True(
            undeclared.Count == 0,
            $"{model.Name} declares properties absent from {schemaPath}: {string.Join(", ", undeclared)}");
    }

    private static string? ReadScalar(YamlDotNet.RepresentationModel.YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlDotNet.RepresentationModel.YamlScalarNode(key), out var value)
            ? (value as YamlDotNet.RepresentationModel.YamlScalarNode)?.Value
            : null;

    private static string ToSnakeCase(string pascalCase)
    {
        var builder = new System.Text.StringBuilder();

        foreach (var character in pascalCase)
        {
            if (char.IsUpper(character) && builder.Length > 0)
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }
}
