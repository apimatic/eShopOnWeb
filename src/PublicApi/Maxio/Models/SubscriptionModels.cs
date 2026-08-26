using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

// Models for the spec's Create-Subscription-Request / Create-Subscription /
// Subscription-Response / Subscription schemas.

public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public required MaxioCreateSubscription Subscription { get; set; }
}

public class MaxioCreateSubscription
{
    /// <summary>API handle of the product (plan) to subscribe to.</summary>
    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; set; }

    /// <summary>Maxio id of an existing customer.</summary>
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>Our app's reference for the subscription (used for idempotent signups).</summary>
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>Spec: Collection-Method (automatic | remittance | prepaid | invoice).</summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}

public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}
