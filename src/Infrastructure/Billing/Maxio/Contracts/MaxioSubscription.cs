using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire shape of the <c>Subscription</c> schema.</summary>
public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Subscription-State enum value, e.g. "active", "trialing", "canceled".</summary>
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

    /// <summary>When capture of payment will next be tried; tracks the end of the current period.</summary>
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

    [JsonPropertyName("cancel_at_end_of_period")]
    public bool? CancelAtEndOfPeriod { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Collection-Method enum value, e.g. "automatic" or "remittance".</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product_price_point_id")]
    public long? ProductPricePointId { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
