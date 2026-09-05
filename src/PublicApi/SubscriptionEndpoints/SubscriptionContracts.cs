using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest : BaseRequest
{
    /// <summary>The Maxio product handle returned by GET /api/subscription-plans.</summary>
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
    public bool RequiresPaymentMethod { get; init; }
}

public sealed class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; } = [];
}

public sealed class MySubscriptionDto
{
    public int Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionDto Subscription { get; init; } = new();
    public bool ExistingSubscription { get; init; }
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; } = [];
}

public sealed class MaxioProductEnvelope { public MaxioProduct Product { get; init; } = new(); }
public sealed class MaxioCustomerEnvelope { public MaxioCustomer Customer { get; init; } = new(); }
public sealed class MaxioSubscriptionEnvelope { public MaxioSubscription Subscription { get; init; } = new(); }

public sealed class MaxioProduct
{
    [JsonPropertyName("handle")]
    public string? Handle { get; init; }
    [JsonPropertyName("name")]
    public string? Name { get; init; }
    [JsonPropertyName("description")]
    public string? Description { get; init; }
    [JsonPropertyName("price_in_cents")]
    public long PriceInCents { get; init; }
    [JsonPropertyName("interval")]
    public int Interval { get; init; }
    [JsonPropertyName("interval_unit")]
    public string? IntervalUnit { get; init; }
    [JsonPropertyName("require_credit_card")]
    public bool RequireCreditCard { get; init; }
    [JsonPropertyName("archived_at")]
    public DateTimeOffset? ArchivedAt { get; init; }
}

public sealed class MaxioCustomer { [JsonPropertyName("id")] public int Id { get; init; } }

public sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public int Id { get; init; }
    [JsonPropertyName("state")]
    public string? State { get; init; }
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; init; }
    [JsonPropertyName("current_period_ends_at")]
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; init; } = new();
}
