using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>The spec's <c>Subscription-Response</c> schema.</summary>
public sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Subset of the spec's <c>Subscription</c> schema that this integration consumes.</summary>
public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>See the spec's <c>Subscription-State</c> enum.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; set; }

    /// <summary>Recurring amount of the subscribed product version, in cents.</summary>
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next charge will be attempted; diverges from the period end while retrying a failed payment.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Our own reference for the subscription, supplied at signup.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>See the spec's <c>Collection-Method</c> enum: "automatic" or "remittance".</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; set; }
}

/// <summary>The spec's <c>Create-Subscription-Request</c> schema.</summary>
public sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Subset of the spec's <c>Create-Subscription</c> schema. This integration always identifies the
/// plan by handle (the spec recommends the handle over the unpublished numeric product id) and the
/// shopper by the id of a customer it has already ensured exists.
/// </summary>
public sealed class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProductPricePointHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>Caller-supplied reference for the subscription. Unique per site, so it doubles as an idempotency key.</summary>
    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }
}
