using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed record MaxioProduct(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("handle")] string? Handle,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("interval")] int Interval,
    [property: JsonPropertyName("interval_unit")] string IntervalUnit,
    [property: JsonPropertyName("archived_at")] DateTimeOffset? ArchivedAt,
    [property: JsonPropertyName("require_credit_card")] bool RequireCreditCard,
    [property: JsonPropertyName("taxable")] bool Taxable);

public sealed record MaxioProductEnvelope(
    [property: JsonPropertyName("product")] MaxioProduct Product);

public sealed record MaxioCustomer(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("email")] string Email);

public sealed record MaxioCustomerEnvelope(
    [property: JsonPropertyName("customer")] MaxioCustomer Customer);

public sealed record MaxioSubscription(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("product_price_in_cents")] long PriceInCents,
    [property: JsonPropertyName("current_period_ends_at")] DateTimeOffset? CurrentPeriodEndsAt,
    [property: JsonPropertyName("next_assessment_at")] DateTimeOffset? NextAssessmentAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("reference")] string? Reference,
    [property: JsonPropertyName("customer")] MaxioCustomer? Customer,
    [property: JsonPropertyName("product")] MaxioProduct? Product);

public sealed record MaxioSubscriptionEnvelope(
    [property: JsonPropertyName("subscription")] MaxioSubscription Subscription);

public sealed class MaxioApiErrorResponse
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}

public sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
