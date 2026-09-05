using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductFamilyResponse
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

public sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;
}

public sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("handle")]
    public string Handle { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public string IntervalUnit { get; init; } = string.Empty;

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }

    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }
}

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("reference")]
    public string? Reference { get; init; }

    [JsonPropertyName("state")]
    public string State { get; init; } = string.Empty;

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}
