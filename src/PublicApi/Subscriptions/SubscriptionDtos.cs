using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanDto
{
    [JsonPropertyName("handle")] public string Handle { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("price")] public decimal? Price { get; init; }
    [JsonPropertyName("interval")] public int? Interval { get; init; }
    [JsonPropertyName("intervalUnit")] public string? IntervalUnit { get; init; }
    [JsonPropertyName("pricePointHandle")] public string? PricePointHandle { get; init; }
    [JsonPropertyName("pricePointId")] public int? PricePointId { get; init; }
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public SubscriptionPlansResponse() { }

    [JsonPropertyName("plans")] public List<SubscriptionPlanDto> Plans { get; } = new();
}

public sealed class SubscribeRequest : BaseRequest
{
    [JsonPropertyName("planHandle")] public string PlanHandle { get; init; } = string.Empty;
    [JsonPropertyName("pricePointHandle")] public string? PricePointHandle { get; init; }
}

public sealed class SubscriptionDto
{
    [JsonPropertyName("id")] public int? Id { get; init; }
    [JsonPropertyName("planHandle")] public string PlanHandle { get; init; } = string.Empty;
    [JsonPropertyName("planName")] public string? PlanName { get; init; }
    [JsonPropertyName("price")] public decimal? Price { get; init; }
    [JsonPropertyName("state")] public string? State { get; init; }
    [JsonPropertyName("nextBillingDate")] public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    [JsonPropertyName("subscription")] public SubscriptionDto? Subscription { get; init; }
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    [JsonPropertyName("subscriptions")] public List<SubscriptionDto> Subscriptions { get; } = new();
}
