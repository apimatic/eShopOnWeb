using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record MaxioPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string? PricePointHandle);

public sealed record MaxioCustomer(int Id, string Reference);

public sealed record MaxioSubscription(
    int Id,
    string? Reference,
    string State,
    long PriceInCents,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt,
    string? ProductHandle,
    string? ProductName,
    string? ProductPricePointHandle);

internal sealed class MaxioCustomerEnvelope
{
    [JsonPropertyName("customer")]
    public MaxioCustomerWire? Customer { get; set; }
}

internal sealed class MaxioCustomerWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}

internal sealed class MaxioProductListItem
{
    [JsonPropertyName("product")]
    public MaxioProductWire? Product { get; set; }
}

internal sealed class MaxioProductWire
{
    [JsonPropertyName("handle")]
    public string? Handle { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }

    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }
}

internal sealed class MaxioSubscriptionEnvelope
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionWire? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscriptionWire? Subscription { get; set; }
}

internal sealed class MaxioSubscriptionWire
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("product_price_in_cents")]
    public long PriceInCents { get; set; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }

    [JsonPropertyName("product")]
    public MaxioProductWire? Product { get; set; }
}

internal sealed class MaxioErrorEnvelope
{
    [JsonPropertyName("errors")]
    public object? Errors { get; set; }
}
