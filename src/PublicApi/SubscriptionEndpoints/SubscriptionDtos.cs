using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanDto
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("priceInCents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("interval")]
    public int? Interval { get; set; }

    [JsonPropertyName("intervalUnit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("requiresPaymentMethod")]
    public bool RequiresPaymentMethod { get; set; }
}

public sealed class SubscriptionDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("planHandle")]
    public string PlanHandle { get; set; } = string.Empty;

    [JsonPropertyName("planName")]
    public string PlanName { get; set; } = string.Empty;

    [JsonPropertyName("priceInCents")]
    public int PriceInCents { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("nextBillingDate")]
    public DateTimeOffset? NextBillingDate { get; set; }

    [JsonPropertyName("activatedAt")]
    public DateTimeOffset? ActivatedAt { get; set; }

    [JsonPropertyName("stale")]
    public bool Stale { get; set; }
}
