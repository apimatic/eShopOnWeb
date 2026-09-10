using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription domain models to their API DTOs, including presentation concerns such as
/// formatting prices for display.
/// </summary>
internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToMajorUnits(plan.PriceInCents),
        FormattedPrice = FormatPrice(plan.PriceInCents, plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit
    };

    public static CustomerSubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = ToMajorUnits(subscription.PriceInCents),
        FormattedPrice = FormatPrice(subscription.PriceInCents, subscription.Interval, subscription.IntervalUnit),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CreatedAt = subscription.CreatedAt
    };

    private static decimal ToMajorUnits(long priceInCents) => priceInCents / 100m;

    private static string FormatPrice(long priceInCents, int interval, string intervalUnit)
    {
        var amount = ToMajorUnits(priceInCents).ToString("C", CultureInfo.GetCultureInfo("en-US"));
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var period = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"{amount} / {period}";
    }
}
