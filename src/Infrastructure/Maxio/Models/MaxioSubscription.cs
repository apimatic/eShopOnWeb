using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio Advanced Billing <c>Subscription</c>.
/// Shape defined by <c>maxio-spec/components/schemas/Subscription.yaml</c>; only the fields
/// eShopOnWeb consumes are bound.
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>One of the values of <c>Subscription-State.yaml</c>, e.g. <c>active</c>.</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("total_revenue_in_cents")]
    public long TotalRevenueInCents { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the next charge will be attempted. Tracks <c>current_period_ends_at</c> except while a
    /// failed payment is being retried, which is why it - not the period end - is reported to the
    /// shopper as the next billing date.
    /// </summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("trial_started_at")]
    public DateTimeOffset? TrialStartedAt { get; set; }

    [JsonPropertyName("trial_ended_at")]
    public DateTimeOffset? TrialEndedAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

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

/// <summary>
/// Maxio <c>Subscription Response</c> envelope.
/// </summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// Body of <c>POST /subscriptions.json</c>, per <c>Create-Subscription-Request.yaml</c>.
/// </summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}

/// <summary>
/// Maxio <c>Create Subscription</c> attributes. eShopOnWeb always identifies an existing customer
/// by <c>customer_reference</c> and the plan by <c>product_handle</c>, per the spec's guidance that
/// numeric product ids are not published.
/// </summary>
public class MaxioCreateSubscription
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>Reference chosen by eShopOnWeb, used to recognise a subscription it created.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>One of the values of <c>Collection-Method.yaml</c>.</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
