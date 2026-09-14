using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio <c>Subscription</c> (<c>maxio-spec/components/schemas/Subscription.yaml</c>).
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>One of the values of the specification <c>Subscription State</c> enumeration.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; set; }

    /// <summary>Recurring amount of the product version this subscription is bound to.</summary>
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>End of the current period, i.e. when the next regular charge falls due.</summary>
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When capture of payment will be tried or retried. Usually tracks
    /// <see cref="CurrentPeriodEndsAt"/>, and diverges after a failed renewal payment.
    /// </summary>
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

    /// <summary>The reference value provided by the subscribing application.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>One of the values of the specification <c>Collection Method</c> enumeration.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public int? ProductPricePointId { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// Maxio <c>Subscription Response</c>
/// (<c>maxio-spec/components/schemas/Subscription-Response.yaml</c>).
/// </summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// Maxio <c>Create Subscription Request</c>
/// (<c>maxio-spec/components/schemas/Create-Subscription-Request.yaml</c>).
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Maxio <c>Create Subscription</c>
/// (<c>maxio-spec/components/schemas/Create-Subscription.yaml</c>).
/// </summary>
/// <remarks>
/// Only the properties eShopOnWeb sends are transcribed. The plan is identified by
/// <see cref="ProductHandle"/> rather than by product id because the specification itself
/// recommends the handle, and because handles survive a catalog re-seed.
/// </remarks>
public class MaxioCreateSubscription
{
    /// <summary>The API handle of the product to subscribe to.</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>The id of an existing customer within Maxio.</summary>
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>The reference value provided by the subscribing application; unique per site.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>One of the values of the specification <c>Collection Method</c> enumeration.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
