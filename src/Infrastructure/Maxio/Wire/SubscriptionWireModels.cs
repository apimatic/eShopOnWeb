using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Wire;

// Wire-format models mirroring maxio-spec/components/schemas/Subscription.yaml,
// Subscription-Response.yaml and Create-Subscription(-Request).yaml.

internal sealed class SubscriptionWire
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
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerWire? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductWire? Product { get; set; }
}

internal sealed class SubscriptionResponseWire
{
    [JsonPropertyName("subscription")]
    public SubscriptionWire? Subscription { get; set; }
}

internal sealed class CreateSubscriptionWire
{
    [JsonPropertyName("product_handle")]
    public string ProductHandle { get; set; } = string.Empty;

    [JsonPropertyName("customer_id")]
    public int CustomerId { get; set; }

    /// <summary>
    /// Fixed to "remittance" so enrollment succeeds without a stored payment method: with the
    /// default "automatic" method, Maxio attempts to charge a card immediately and the create
    /// fails (422) when none is on file. Remittance instead issues an invoice for the buyer to
    /// pay later, matching plans configured with payment method not required.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}

internal sealed class CreateSubscriptionRequestWire
{
    [JsonPropertyName("subscription")]
    public CreateSubscriptionWire Subscription { get; set; } = new();
}
