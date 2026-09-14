using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

internal sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProductJson? Product { get; set; }
}

internal sealed class MaxioProductJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamilyJson? ProductFamily { get; set; }
}

internal sealed class MaxioProductFamilyJson
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }
}

internal sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomerJson? Customer { get; set; }
}

internal sealed class MaxioCustomerJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionJson? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionJson
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long? ProductPriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("customer")]
    public MaxioCustomerJson? Customer { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductJson? Product { get; set; }
}
