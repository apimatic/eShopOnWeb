using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps billing domain models to the PublicApi DTOs.</summary>
internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInDollars,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        FormattedPrice = FormatPrice(plan.PriceInDollars, plan.Interval, plan.IntervalUnit)
    };

    public static CustomerSubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInDollars,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static string FormatPrice(decimal price, int interval, string intervalUnit)
    {
        var amount = price.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var period = interval > 1 ? $"every {interval} {intervalUnit}s" : intervalUnit;
        return $"{amount}/{period}";
    }
}
