using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

internal sealed class MaxioSubscription
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

    /// <summary>
    /// When the next charge will be attempted. Tracks current_period_ends_at except after a
    /// failed renewal, when it moves to the retry time - so it is the honest "next billing" value.
    /// </summary>
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("activated_at")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("canceled_at")]
    public DateTimeOffset? CanceledAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; set; }

    /// <summary>Null on sites using the catalog-independent subscription experience.</summary>
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; set; }
}

internal sealed class MaxioCreateSubscriptionRequest
{
    [JsonPropertyName("subscription")]
    public MaxioCreateSubscriptionAttributes Subscription { get; set; } = new();

    /// <summary>
    /// Duplicate-prevention token. A repeat of the same POST within 60 minutes is rejected with
    /// 409 instead of creating a second subscription.
    /// </summary>
    [JsonPropertyName("uniqueness_token")]
    public string? UniquenessToken { get; set; }
}

internal sealed class MaxioCreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string? ProductHandle { get; set; }

    [JsonPropertyName("customer_id")]
    public long? CustomerId { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    /// <summary>
    /// How Maxio collects payment. Omitted, it defaults to the site setting - which is usually
    /// "automatic", and that fails at signup when no payment profile is on file.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string? PaymentCollectionMethod { get; set; }
}
