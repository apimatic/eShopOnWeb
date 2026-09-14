using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    decimal Price,
    int BillingInterval,
    string BillingIntervalUnit);

public sealed record SubscriptionDto(
    long Id,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    decimal Price,
    int BillingInterval,
    string BillingIntervalUnit,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed class SubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = null!;
}

public sealed class MySubscriptionsResponse : BaseResponse
{
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
