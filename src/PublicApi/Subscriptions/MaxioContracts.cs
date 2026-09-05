using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductResponse
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProductListItem
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}

public sealed class MaxioProduct
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
    [JsonPropertyName("product_family")]
    public MaxioProductFamily? ProductFamily { get; init; }
}

public sealed class MaxioProductFamily
{
    public string? Handle { get; init; }
}

public sealed class MaxioCustomerResponse
{
    [JsonPropertyName("customer")]
    public MaxioCustomer Customer { get; init; } = new();
}

public sealed class MaxioCustomer
{
    public int Id { get; init; }
}

public sealed class MaxioSubscriptionResponse
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscriptionListItem
{
    [JsonPropertyName("subscription")]
    public MaxioSubscription Subscription { get; init; } = new();
}

public sealed class MaxioSubscription
{
    public int Id { get; init; }
    public string? State { get; init; }
    public string? Reference { get; init; }
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; init; }
    public MaxioProduct? Product { get; init; }
}

public sealed record CreateMaxioCustomer(string FirstName, string LastName, string Email, string Reference);
public sealed record CreateMaxioSubscription(string ProductHandle, int CustomerId, string Reference, DateTimeOffset NextBillingAt);
