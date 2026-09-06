using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio <c>Subscription</c> schema (components/schemas/Subscription.yaml). Only the fields this
/// integration consumes are mapped; unmapped fields are ignored on deserialization.
/// </summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

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
    /// When the provider will next attempt to capture payment. Normally tracks
    /// <see cref="CurrentPeriodEndsAt"/>, but diverges while a renewal payment is being retried.
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

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>Maxio <c>Subscription-Response</c> envelope.</summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>
/// Maxio <c>Create-Subscription</c> schema. Only the properties this integration sets are
/// declared; null properties are omitted from the serialized request body.
/// </summary>
public class MaxioCreateSubscription
{
    /// <summary>API handle of the product to subscribe to. Preferred over the unstable numeric id.</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    /// <summary>Id of an existing Maxio customer to enrol.</summary>
    [JsonPropertyName("customer_id")]
    public int? CustomerId { get; set; }

    /// <summary>Reference of an existing Maxio customer, as an alternative to <see cref="CustomerId"/>.</summary>
    [JsonPropertyName("customer_reference")]
    public string? CustomerReference { get; set; }

    /// <summary>Attributes used to create the customer alongside the subscription.</summary>
    [JsonPropertyName("customer_attributes")]
    public MaxioCreateCustomer? CustomerAttributes { get; set; }

    /// <summary>Caller-supplied reference for the subscription itself. Unique per Maxio site.</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>
    /// Maxio <c>Collection-Method</c>: how the subscription is billed. <c>automatic</c> captures
    /// from a stored payment profile; the invoice-based methods do not need one.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

/// <summary>Maxio <c>Create-Subscription-Request</c> envelope.</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscription Subscription { get; set; } = new();
}
