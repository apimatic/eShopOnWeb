using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int Interval,
    string IntervalUnit,
    string Currency)
{
    public static SubscriptionPlanDto From(SubscriptionPlan plan) =>
        new(plan.Handle, plan.Name, plan.Description, plan.PriceInCents, plan.Interval, plan.IntervalUnit, plan.Currency);
}

public sealed record SubscriptionPlanSummaryDto(string Handle, string Name);

public sealed record SubscriptionDto(
    string Reference,
    SubscriptionPlanSummaryDto Plan,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate)
{
    public static SubscriptionDto From(SubscriptionConfirmation subscription) =>
        new(
            subscription.Reference,
            new SubscriptionPlanSummaryDto(subscription.ProductHandle, subscription.ProductName),
            subscription.PriceInCents,
            subscription.State,
            subscription.NextBillingDate);
}

public sealed record SubscriptionPlansResponse(IReadOnlyList<SubscriptionPlanDto> Plans);
public sealed record MySubscriptionsResponse(IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

