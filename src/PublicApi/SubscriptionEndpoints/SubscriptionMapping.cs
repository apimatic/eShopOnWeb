using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing domain onto the API contract.
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
        BillingPeriod = DescribePeriod(plan.Interval, plan.IntervalUnit),
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        TrialPrice = plan.TrialPriceInCents is null ? null : decimal.Divide(plan.TrialPriceInCents.Value, 100m)
    };

    public static SubscriptionDto ToDto(this SubscriberSubscription subscription) => new()
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
        BillingPeriod = DescribePeriod(subscription.Interval, subscription.IntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        TrialEndsAt = subscription.TrialEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Balance = decimal.Divide(subscription.BalanceInCents, 100m),
        Reference = subscription.Reference,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>
    /// Renders a billing interval as prose: "every month", "every 3 months", "every 30 days".
    /// </summary>
    private static string DescribePeriod(int interval, string? intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit) || interval <= 0)
        {
            return string.Empty;
        }

        return interval == 1
            ? $"every {intervalUnit}"
            : $"every {interval.ToString(CultureInfo.InvariantCulture)} {intervalUnit}s";
    }
}
