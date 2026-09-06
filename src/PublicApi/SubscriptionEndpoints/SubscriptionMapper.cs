using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing-agnostic subscription models onto the API contract.
/// </summary>
public static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingSummary = FormatBillingSummary(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit),
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        InitialCharge = plan.InitialChargeInCents / 100m,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        PricePointName = plan.PricePointName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        IsActive = subscription.IsActive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingSummary = FormatBillingSummary(
            subscription.Price, subscription.Currency, subscription.Interval, subscription.IntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        Balance = subscription.BalanceInCents / 100m,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>Renders a price and cadence as a single display string, e.g. "299.00 USD / month".</summary>
    internal static string FormatBillingSummary(decimal price, string currency, int interval, string intervalUnit)
    {
        var amount = price.ToString("0.00", CultureInfo.InvariantCulture);
        var money = string.IsNullOrWhiteSpace(currency) ? amount : $"{amount} {currency}";

        if (interval <= 0 || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return money;
        }

        var cadence = interval == 1 ? intervalUnit : $"{interval.ToString(CultureInfo.InvariantCulture)} {intervalUnit}s";
        return $"{money} / {cadence}";
    }
}
