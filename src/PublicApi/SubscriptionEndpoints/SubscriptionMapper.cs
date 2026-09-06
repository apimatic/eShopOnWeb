using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the subscription domain onto the API contract.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = DescribePeriod(plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        PricePointName = plan.PricePointName,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.RawState,
        IsActive = subscription.State.IsOccupied(),
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingPeriod = DescribePeriod(subscription.Interval, subscription.IntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        Balance = subscription.Balance,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod
    };

    /// <summary>
    /// Renders a billing period the way a shopper would read it: "month", not "1 month".
    /// </summary>
    private static string DescribePeriod(int interval, string intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        return interval <= 1
            ? intervalUnit
            : string.Create(CultureInfo.InvariantCulture, $"{interval} {intervalUnit}s");
    }
}
