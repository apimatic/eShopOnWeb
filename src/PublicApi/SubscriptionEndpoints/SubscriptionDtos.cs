using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public sealed record SubscriptionPlanDto(
    int MaxioProductId,
    string Name,
    string Handle,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record SubscriptionDto(
    int MaxioSubscriptionId,
    string PlanName,
    string PlanHandle,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate);

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record SubscribeResponse(SubscriptionDto Subscription);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto Map(SubscriptionPlan plan) => new(
        plan.MaxioProductId,
        plan.Name,
        plan.Handle,
        plan.Description,
        plan.PriceInCents,
        plan.Interval,
        plan.IntervalUnit);

    public static SubscriptionDto Map(CustomerSubscription subscription) => new(
        subscription.MaxioSubscriptionId,
        subscription.PlanName,
        subscription.PlanHandle,
        subscription.PriceInCents,
        subscription.State,
        subscription.NextBillingDate);

    public static IReadOnlyList<SubscriptionPlanDto> Map(IReadOnlyList<SubscriptionPlan> plans) =>
        plans.Select(Map).ToList();

    public static IReadOnlyList<SubscriptionDto> Map(IReadOnlyList<CustomerSubscription> subscriptions) =>
        subscriptions.Select(Map).ToList();
}
