using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the billing-agnostic domain models onto the API DTOs, deriving display-friendly fields
/// (decimal price and a formatted price string) from the raw cents values.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Id = plan.Id,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = ToDecimal(plan.PriceInCents),
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PriceDisplay = FormatPrice(plan.PriceInCents, plan.Currency, plan.Interval, plan.IntervalUnit),
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanName = subscription.PlanName,
        PlanHandle = subscription.PlanHandle,
        PriceInCents = subscription.PriceInCents,
        Price = ToDecimal(subscription.PriceInCents),
        Currency = subscription.Currency,
        PriceDisplay = FormatPrice(subscription.PriceInCents, subscription.Currency, interval: 1, intervalUnit: null),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingAt,
        CreatedAt = subscription.CreatedAt
    };

    private static decimal ToDecimal(long cents) => cents / 100m;

    private static string FormatPrice(long cents, string currency, int interval, string? intervalUnit)
    {
        var amount = ToDecimal(cents).ToString("0.00", CultureInfo.InvariantCulture);
        var symbol = CurrencySymbol(currency);
        var money = $"{symbol}{amount}";

        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return money;
        }

        return interval <= 1
            ? $"{money} / {intervalUnit}"
            : $"{money} every {interval} {intervalUnit}s";
    }

    private static string CurrencySymbol(string currency) => currency?.ToUpperInvariant() switch
    {
        "USD" => "$",
        "EUR" => "€",
        "GBP" => "£",
        _ => string.IsNullOrWhiteSpace(currency) ? "$" : currency + " "
    };
}
