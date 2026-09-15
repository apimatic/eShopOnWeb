using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

public sealed class MaxioProduct
{
    public long Id { get; set; }
    public string? Handle { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; set; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; set; }
    [JsonPropertyName("product_price_point_id")]
    public long? ProductPricePointId { get; set; }
    [JsonPropertyName("product_price_point_handle")]
    public string? ProductPricePointHandle { get; set; }
}

public sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

public sealed class MaxioCustomer
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }
    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

public sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscription
{
    public long Id { get; set; }
    public string? Reference { get; set; }
    public string? State { get; set; }
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public MaxioCustomer? Customer { get; set; }
    public MaxioProduct? Product { get; set; }
}

public sealed class MaxioSubscriptionListEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

public sealed class MaxioSubscriptionListResponse
{
    public List<MaxioSubscriptionListEnvelope> Items { get; set; } = new();
}
