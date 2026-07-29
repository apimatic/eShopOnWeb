using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps billing-provider-agnostic domain records onto the API DTOs, formatting money once.</summary>
public static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToDollars(plan.PriceInCents),
        PriceDisplay = ToDisplay(plan.PriceInCents),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        ProductId = plan.ProductId
    };

    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = ToDollars(subscription.PriceInCents),
        PriceDisplay = ToDisplay(subscription.PriceInCents),
        NextBillingDate = subscription.CurrentPeriodEndsAt
    };

    private static decimal ToDollars(long cents) => cents / 100m;

    private static string ToDisplay(long cents) =>
        ToDollars(cents).ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
