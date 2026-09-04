using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

internal sealed class MaxioProductEnvelope
{
    [JsonPropertyName("product")]
    public MaxioProduct Product { get; set; } = new();
}

internal sealed class MaxioProduct
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
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
}

internal sealed class MaxioCustomerEnvelope
{
    public MaxioCustomer Customer { get; set; } = new();
}

internal sealed class MaxioCustomer
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
}

internal sealed class MaxioSubscriptionEnvelope
{
    public MaxioSubscription Subscription { get; set; } = new();
}

internal sealed class MaxioSubscription
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
    [JsonPropertyName("reference")]
    public string Reference { get; set; } = string.Empty;
    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("product_price_in_cents")]
    public long ProductPriceInCents { get; set; }
    [JsonPropertyName("next_assessment_at")]
    public DateTimeOffset? NextAssessmentAt { get; set; }
    [JsonPropertyName("product")]
    public MaxioSubscriptionProduct? Product { get; set; }
}

internal sealed class MaxioSubscriptionProduct
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("handle")]
    public string Handle { get; set; } = string.Empty;
}

public sealed class SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

public sealed class SubscriptionDto
{
    public long Id { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; init; }
}

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<SubscriptionPlanDto> Plans { get; init; } = Array.Empty<SubscriptionPlanDto>();
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<SubscriptionDto> Subscriptions { get; init; } = Array.Empty<SubscriptionDto>();
}

public sealed class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId, SubscriptionDto subscription) : base(correlationId)
    {
        Subscription = subscription;
    }

    public SubscriptionDto Subscription { get; init; }
}

public sealed class SubscribeRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
}
