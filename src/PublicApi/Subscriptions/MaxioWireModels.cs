using System;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

// These types deliberately model only fields confirmed in the Advanced Billing API
// responses and required by this feature.
internal sealed class MaxioProductFamilyResponse
{
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

internal sealed class MaxioProductFamily
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
}

internal sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("handle")]
    public string? Handle { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("price_in_cents")]
    public int PriceInCents { get; init; }

    [JsonPropertyName("interval")]
    public int Interval { get; init; }

    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

internal sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer? Customer { get; init; }
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
}

internal sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription? Subscription { get; init; }
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonPropertyName("product_price_in_cents")]
    public int ProductPriceInCents { get; init; }

    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }

    [JsonPropertyName("product")]
    public MaxioProduct? Product { get; init; }
}
