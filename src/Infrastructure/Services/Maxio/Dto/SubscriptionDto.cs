using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio.Dto;

internal class SubscriptionEnvelopeDto
{
    [JsonPropertyName("subscription")]
    public SubscriptionDto? Subscription { get; set; }
}

internal class SubscriptionDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

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
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerDto? Customer { get; set; }

    [JsonPropertyName("product")]
    public ProductDto? Product { get; set; }
}

internal class CreateSubscriptionEnvelopeDto
{
    [JsonPropertyName("subscription")]
    public required CreateSubscriptionDto Subscription { get; set; }
}

internal class CreateSubscriptionDto
{
    [JsonPropertyName("customer_id")]
    public required long CustomerId { get; set; }

    [JsonPropertyName("product_handle")]
    public required string ProductHandle { get; set; }

    /// <summary>
    /// The sandbox's seeded plans have no payment method required, so subscriptions are
    /// created for remittance (invoiced) collection rather than "automatic", which would
    /// otherwise attempt to charge a card that was never captured and fail signup.
    /// </summary>
    [JsonPropertyName("payment_collection_method")]
    public string PaymentCollectionMethod { get; set; } = "remittance";
}
