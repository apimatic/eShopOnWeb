using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscribeRequest
{
    public string? PlanHandle { get; set; }
    public string? ProductPricePointHandle { get; set; }
}

public sealed record SubscriptionPlanResponse(
    string Handle,
    string Name,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? ProductPricePointHandle,
    int? ProductPricePointId,
    bool? Taxable);

public sealed record SubscriptionResponse(
    int? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    long? CurrentBillingAmountInCents,
    string? State,
    DateTimeOffset? NextBillingDate,
    string? Currency);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanResponse> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionResponse> Subscriptions);
