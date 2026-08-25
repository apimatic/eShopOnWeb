using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

/// <summary>Subscription per the Maxio OpenAPI spec (Subscription schema).</summary>
public class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTime? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTime? CurrentPeriodEndsAt { get; set; }

    /// <summary>Per the spec, when the next payment capture will be tried; tracks current_period_ends_at for healthy subscriptions.</summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTime? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTime? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTime? CanceledAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

/// <summary>Wrapper per the spec's Subscription-Response schema ({ "subscription": { ... } }).</summary>
public class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

/// <summary>Request body per the spec's Create-Subscription-Request schema.</summary>
public class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();
}

public class MaxioCreateSubscriptionAttributes
{
    /// <summary>The API handle of the product to subscribe to (alternative to product_id per the spec).</summary>
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>The ID of an existing Maxio customer.</summary>
    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// Per the spec's Collection-Method schema. "remittance" = invoice-based collection,
    /// so signup succeeds without a card on file for plans that don't require one.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
