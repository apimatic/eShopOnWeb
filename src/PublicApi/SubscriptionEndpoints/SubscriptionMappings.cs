using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Maps billing domain models to the PublicApi DTOs (incl. human-readable price formatting).</summary>
public static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        ProductId = plan.ProductId,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PriceDisplay = FormatRecurringPrice(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit),
    };

    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        State = subscription.State,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingDate = subscription.NextBillingDate,
        PriceDisplay = FormatPrice(subscription.Price, subscription.Currency),
    };

    private static string FormatPrice(decimal price, string currency)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
            return price.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
        return $"{price:0.00} {currency}";
    }

    private static string FormatRecurringPrice(decimal price, string currency, int interval, string intervalUnit)
    {
        var money = FormatPrice(price, currency);
        if (string.IsNullOrWhiteSpace(intervalUnit))
            return money;
        var unit = interval > 1 ? $"{interval} {intervalUnit}s" : intervalUnit;
        return $"{money} / {unit}";
    }
}
