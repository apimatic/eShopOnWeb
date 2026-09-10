using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps ApplicationCore subscription models to their API DTOs.</summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        PriceDisplay = FormatPrice(plan.PriceInCents, plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        PriceDisplay = FormatPrice(subscription.PriceInCents, subscription.Interval, subscription.IntervalUnit),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static string FormatPrice(int priceInCents, int interval, string intervalUnit)
    {
        var amount = (priceInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(intervalUnit))
        {
            return $"${amount}";
        }

        var period = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"${amount} / {period}";
    }
}
