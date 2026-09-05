using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These types intentionally model only the fields used from maxio-spec/openapi.yaml.
internal sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
}

internal sealed class MaxioCreateCustomerRequest
{
    [JsonPropertyName("customer")]
    public required MaxioCreateCustomer Customer { get; init; }
}

internal sealed class MaxioCreateCustomer
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

internal sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

internal sealed class MaxioProduct
{
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

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required MaxioCreateSubscription Subscription { get; init; }
}

internal sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; init; }

    [JsonPropertyName("reference")]
    public required string Reference { get; init; }

    [JsonPropertyName("payment_collection_method")]
    public required string PaymentCollectionMethod { get; init; }
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

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDetails(
    int Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt);

internal sealed class MaxioApiException : Exception
{
    public MaxioApiException(int statusCode) : base("Maxio Advanced Billing returned an unsuccessful response.")
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}

internal sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException() : base("The requested subscription plan is not available.") { }
}
