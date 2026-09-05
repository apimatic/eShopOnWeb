using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

// These types deliberately mirror the request/response wrappers and fields used
// by maxio-spec/openapi.yaml, rather than relying on undocumented API shapes.
public sealed record MaxioProductResponse([property: JsonPropertyName("product")] MaxioProduct Product);

public sealed record MaxioProduct(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt);

public sealed record MaxioCustomerResponse([property: JsonPropertyName("customer")] MaxioCustomer Customer);

public sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("reference")] string? Reference);

public sealed record MaxioSubscriptionResponse([property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

public sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("product_price_in_cents")] long ProductPriceInCents,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("product")] MaxioSubscriptionProduct Product);

public sealed record MaxioSubscriptionProduct(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit);

public sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public CreateCustomer Customer { get; init; } = new();
}

public sealed class CreateCustomer
{
    [JsonPropertyName("first_name")]
    public string FirstName { get; init; } = string.Empty;

    [JsonPropertyName("last_name")]
    public string LastName { get; init; } = string.Empty;

    [JsonPropertyName("email")]
    public string Email { get; init; } = string.Empty;

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}

public sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateSubscription Subscription { get; init; } = new();
}

public sealed class CreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; init; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; init; }

    // Collection-Method.yaml in the Maxio contract permits invoice. It lets this
    // no-card signup create the subscription with its recurring balance invoiced.
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; init; } = "invoice";

    [JsonPropertyName("reference")]
    public string Reference { get; init; } = string.Empty;
}
