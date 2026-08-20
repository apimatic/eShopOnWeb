using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed record MaxioCustomer(long Id);

public sealed record MaxioSubscription(
    long Id,
    long CustomerId,
    string PlanHandle,
    string PlanName,
    string ProductFamilyHandle,
    string PricePointName,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string State,
    DateTimeOffset? NextBillingAt);

internal sealed class ProductEnvelope
{
    [JsonPropertyName("product")]
    public ProductData Product { get; set; } = new();
}

internal sealed class ProductData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; set; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }

    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; set; }

    [JsonPropertyName("product_family")]
    public ProductFamilyData ProductFamily { get; set; } = new();

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }
}

internal sealed class ProductFamilyData
{
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

internal sealed class CustomerEnvelope
{
    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();
}

internal sealed class CustomerData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

internal sealed class SubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public SubscriptionData Subscription { get; set; } = new();
}

internal sealed class SubscriptionData
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("customer")]
    public CustomerData Customer { get; set; } = new();

    [JsonPropertyName("product")]
    public ProductData? Product { get; set; }

    [JsonPropertyName("product_price_point_name")]
    public string? ProductPricePointName { get; set; }
}
