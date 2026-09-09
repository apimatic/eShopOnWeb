using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps ApplicationCore subscription models to the PublicApi DTOs.</summary>
internal static class SubscriptionDtoMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PriceFormatted = FormatPrice(plan.Price, plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static MySubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static string FormatPrice(decimal price, int interval, string intervalUnit)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var cadence = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"{amount} / {cadence}";
    }
}
