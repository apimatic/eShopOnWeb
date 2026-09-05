using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class SubscriptionWire
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }

    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }
}

internal class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal class CreateSubscriptionAttributes
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public long CustomerId { get; set; }

    /// <summary>
    /// The seeded demo plans require no payment method ("payment method not required"),
    /// but Maxio's default "automatic" collection still attempts an immediate card charge
    /// for a non-trial paid product and rejects signup when no card is on file. "invoice"
    /// defers billing instead, so subscribe succeeds without card capture.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "invoice";
}

internal class CreateSubscriptionBody
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionAttributes Subscription { get; set; } = new();

    [JsonPropertyName("uniqueness_token")]
    public string UniquenessToken { get; set; } = string.Empty;
}
