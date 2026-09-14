using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Envelope Maxio wraps a subscription in, both on read and on create.</summary>
internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("balance_in_cents")]
    public long BalanceInCents { get; set; }

    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }

    [JsonPropertyName("current_period_started_at")]
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>
    /// When the next renewal charge is assessed. Normally tracks current_period_ends_at, but diverges
    /// to the retry time after a failed payment.
    /// </summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null for subscriptions built purely from components rather than a catalog product.</summary>
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }
}

/// <summary>Body of <c>POST /subscriptions.json</c>.</summary>
internal sealed class CreateMaxioSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public CreateMaxioSubscription Subscription { get; set; } = new();

    /// <summary>
    /// Long random value that lets Maxio reject a duplicate submission of this same request within
    /// 60 minutes with 409 Conflict rather than creating a second subscription.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UniquenessToken { get; set; }
}

internal sealed class CreateMaxioSubscription
{
    /// <summary>Plan handle. Preferred over the numeric product id, which changes when a site is re-seeded.</summary>
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    [JsonPropertyName("reference")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reference { get; set; }

    [JsonPropertyName("payment_collection_method")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PaymentCollectionMethod { get; set; }
}
