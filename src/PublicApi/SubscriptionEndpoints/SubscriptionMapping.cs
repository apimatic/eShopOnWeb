using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing models onto the API contract.
/// </summary>
internal static class SubscriptionMapping
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
        BillingPeriod = DescribeBillingPeriod(plan.Price, plan.Currency, plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingPeriod = DescribeBillingPeriod(
            subscription.Price, subscription.Currency, subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        TrialEndsAt = subscription.TrialEndsAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>
    /// Renders the cadence the way it reads on a pricing page: "$299.00 / month", or
    /// "$299.00 / 3 months" when a period spans more than one unit.
    /// </summary>
    private static string DescribeBillingPeriod(decimal price, string currency, int interval, string intervalUnit)
    {
        var amount = price.ToString("N2", CultureInfo.InvariantCulture);
        var money = string.Equals(currency, "USD", System.StringComparison.OrdinalIgnoreCase)
            ? $"${amount}"
            : $"{amount} {currency}";

        if (string.IsNullOrWhiteSpace(intervalUnit) || interval <= 0)
        {
            return money;
        }

        var period = interval == 1 ? intervalUnit : $"{interval} {intervalUnit}s";

        return $"{money} / {period}";
    }
}
