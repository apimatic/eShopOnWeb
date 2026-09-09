using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio.Models;

/// <summary>Wrapper for a single subscription, per the spec's <c>Subscription-Response</c> schema.</summary>
public sealed record MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

/// <summary>A billing-system subscription. Fields mirror the spec's <c>Subscription</c> schema (subset used here).</summary>
public sealed record MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

/// <summary>Request body for <c>POST /subscriptions.json</c>, per the spec's <c>Create-Subscription-Request</c>.</summary>
public sealed record CreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required CreateSubscriptionBody Subscription { get; init; }
}

/// <summary>
/// The subscription attributes to create, per the spec's <c>Create-Subscription</c> schema (subset used here).
/// The product is referenced by <c>product_handle</c> and the existing customer by <c>customer_id</c>.
/// <c>payment_collection_method: remittance</c> lets a subscription be created without a stored payment method.
/// </summary>
public sealed record CreateSubscriptionBody
{
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; init; }

    [JsonPropertyName("customer_id")]
    public required long CustomerId { get; init; }

    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; init; } = "remittance";
}
