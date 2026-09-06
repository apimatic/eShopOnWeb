using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing model onto the API's transport types.
/// </summary>
internal static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        // The specification's Product schema carries no currency, so the cadence is rendered without one.
        PriceDescription = DescribePrice(plan.PriceInCents, plan.Interval, plan.IntervalUnit, currency: null),
        HasTrial = plan.TrialInterval is > 0,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        TrialPrice = ToMajorUnits(plan.TrialPriceInCents),
        SetupFee = ToMajorUnits(plan.InitialChargeInCents),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        Taxable = plan.Taxable,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        PricePointName = plan.PricePointName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PriceDescription = DescribePrice(subscription.PriceInCents, subscription.Interval, subscription.IntervalUnit, subscription.Currency),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        TrialEndedAt = subscription.TrialEndedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        CreatedAt = subscription.CreatedAt,
        Balance = subscription.BalanceInCents / 100m,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Reference = subscription.Reference,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    private static decimal? ToMajorUnits(long? cents) => cents is null ? null : cents.Value / 100m;

    /// <summary>
    /// Renders a billing cadence such as "299.00 USD / month" or "29.00 USD / 3 months". The ISO
    /// currency code is used rather than a symbol, because the site's currency is whatever Maxio
    /// reports and guessing a symbol from the host's culture would misprice the plan.
    /// </summary>
    private static string DescribePrice(long priceInCents, int? interval, string? intervalUnit, string? currency)
    {
        var amount = (priceInCents / 100m).ToString("N2", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(currency))
        {
            amount = $"{amount} {currency.Trim().ToUpperInvariant()}";
        }

        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return amount;
        }

        var count = interval ?? 1;
        var unit = count == 1
            ? intervalUnit
            : intervalUnit + (intervalUnit.EndsWith('s') ? string.Empty : "s");

        return count == 1
            ? $"{amount} / {unit}"
            : $"{amount} / {count.ToString(CultureInfo.InvariantCulture)} {unit}";
    }
}
