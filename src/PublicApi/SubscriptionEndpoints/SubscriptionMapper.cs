using System.Collections.Generic;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Projects the billing abstraction's models onto the API's wire contract.</summary>
internal static class SubscriptionMapper
{
    /// <summary>
    /// Symbols for the currencies this storefront is likely to quote. Anything else falls back to the
    /// ISO code, which is unambiguous — better than guessing a symbol and getting it wrong.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CurrencySymbols =
        new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "$",
            ["EUR"] = "€",
            ["GBP"] = "£",
            ["CAD"] = "CA$",
            ["AUD"] = "A$"
        };

    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        FormattedPrice = FormatMoney(plan.PriceInCents, plan.Currency),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = FormatBillingPeriod(plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        FormattedPrice = FormatMoney(subscription.PriceInCents, subscription.Currency),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingPeriod = FormatBillingPeriod(subscription.Interval, subscription.IntervalUnit),
        NextBillingDate = subscription.NextBillingDate,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        Balance = subscription.Balance
    };

    internal static string FormatMoney(long amountInCents, string currency)
    {
        var amount = (amountInCents / 100m).ToString("0.00", CultureInfo.InvariantCulture);
        return CurrencySymbols.TryGetValue(currency ?? string.Empty, out var symbol)
            ? symbol + amount
            : $"{amount} {currency}".Trim();
    }

    internal static string FormatBillingPeriod(int interval, string intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        return interval <= 1 ? $"every {intervalUnit}" : $"every {interval} {intervalUnit}s";
    }
}
