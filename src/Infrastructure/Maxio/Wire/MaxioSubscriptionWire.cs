using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

internal class MaxioSubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductWire? Product { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerWire? Customer { get; set; }
}

internal class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionWire? Subscription { get; set; }
}

internal class CreateMaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public CreateMaxioSubscriptionWire Subscription { get; set; } = new();
}

internal class CreateMaxioSubscriptionWire
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_reference")]
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>
    /// "remittance" is used because these plans don't require a payment method up front
    /// (no card is captured by this flow) — billing is settled out-of-band via invoice.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
