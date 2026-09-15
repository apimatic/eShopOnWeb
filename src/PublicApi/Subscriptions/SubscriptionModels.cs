using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    long? PriceInCents);

public sealed record SubscriptionSummary(
    int? Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlan> Plans);

public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionSummary> Subscriptions);

public sealed class SubscribeRequest
{
    public string? PlanHandle { get; set; }
}
