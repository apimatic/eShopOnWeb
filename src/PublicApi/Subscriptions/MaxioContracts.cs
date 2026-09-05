using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These wire contracts intentionally mirror the request/response schemas in maxio-spec/openapi.yaml.
internal sealed class CreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required CreateCustomer Customer { get; init; }
}

internal sealed class CreateCustomer
{
    [JsonPropertyName("first_name")]
    public required string FirstName { get; init; }
    [JsonPropertyName("last_name")]
    public required string LastName { get; init; }
    [JsonPropertyName("email")]
    public required string Email { get; init; }
    [JsonPropertyName("reference")]
    public required string Reference { get; init; }
}

internal sealed class CustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

internal sealed class CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required CreateSubscription Subscription { get; init; }
}

internal sealed class CreateSubscription
{
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }
    [JsonPropertyName("customer_id")]
    public int CustomerId { get; init; }
    [JsonPropertyName("payment_collection_method")]
    public required string PaymentCollectionMethod { get; init; }
    [JsonPropertyName("reference")]
    public required string Reference { get; init; }
}

internal sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
    [JsonPropertyName("state")]
    public string? State { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

internal sealed class ProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    [JsonPropertyName("interval")]
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

internal sealed class MaxioErrorResponse
{
    [JsonPropertyName("errors")]
    public JsonElement Errors { get; init; }
}
