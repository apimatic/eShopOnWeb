using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        PlanHandle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        BillingPeriod = FormatPeriod(plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PaymentMethodRequired = plan.PaymentMethodRequired
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        BillingPeriod = subscription.Interval.HasValue
            ? FormatPeriod(subscription.Interval.Value, subscription.IntervalUnit)
            : null,
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        BalanceDue = subscription.Balance,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BillingCustomerId = subscription.CustomerId,
        BillingCustomerReference = subscription.CustomerReference
    };

    private static string FormatPeriod(int interval, string? intervalUnit)
    {
        var unit = string.IsNullOrWhiteSpace(intervalUnit) ? "period" : intervalUnit!;
        var plural = interval == 1 ? unit : unit + "s";

        return string.Format(CultureInfo.InvariantCulture, "{0} {1}", interval, plural);
    }
}
