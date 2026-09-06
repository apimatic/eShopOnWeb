using System.Collections.Generic;
using System.Net.Http;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The Maxio Advanced Billing operations this integration calls, transcribed from the
/// OpenAPI specification (maxio-spec/openapi.yaml). Everything - path template, HTTP method,
/// query parameter names - comes from the spec, and the spec conformance tests assert that
/// each entry below still matches it.
/// </summary>
public static class MaxioOperations
{
    /// <summary>A single spec operation.</summary>
    /// <param name="OperationId">The spec's operationId.</param>
    /// <param name="Method">HTTP method as declared by the spec.</param>
    /// <param name="PathTemplate">Path exactly as keyed in the spec's paths object.</param>
    /// <param name="QueryParameters">Query parameters this integration sends.</param>
    public sealed record Operation(string OperationId, HttpMethod Method, string PathTemplate, IReadOnlyList<string> QueryParameters);

    public static readonly Operation ListProductsForProductFamily = new(
        "listProductsForProductFamily",
        HttpMethod.Get,
        "/product_families/{product_family_id}/products.json",
        new[] { "include_archived" });

    public static readonly Operation ReadCustomerByReference = new(
        "readCustomerByReference",
        HttpMethod.Get,
        "/customers/lookup.json",
        new[] { "reference" });

    public static readonly Operation CreateCustomer = new(
        "createCustomer",
        HttpMethod.Post,
        "/customers.json",
        new string[0]);

    public static readonly Operation ListCustomerSubscriptions = new(
        "listCustomerSubscriptions",
        HttpMethod.Get,
        "/customers/{customer_id}/subscriptions.json",
        new string[0]);

    public static readonly Operation FindSubscription = new(
        "findSubscription",
        HttpMethod.Get,
        "/subscriptions/lookup.json",
        new[] { "reference" });

    public static readonly Operation CreateSubscription = new(
        "createSubscription",
        HttpMethod.Post,
        "/subscriptions.json",
        new string[0]);

    public static IReadOnlyList<Operation> All { get; } = new[]
    {
        ListProductsForProductFamily,
        ReadCustomerByReference,
        CreateCustomer,
        ListCustomerSubscriptions,
        FindSubscription,
        CreateSubscription
    };
}
