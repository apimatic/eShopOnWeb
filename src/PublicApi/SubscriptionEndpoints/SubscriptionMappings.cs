using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps the billing models onto the API contract. Kept explicit rather than convention based so a
/// change to the billing models cannot silently change the published API shape.
/// </summary>
public static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Id = plan.Id,
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
        TrialPriceInCents = plan.TrialPriceInCents,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        PricePointName = plan.PricePointName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
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
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CustomerEmail = subscription.CustomerEmail
    };

    private static string DescribePeriod(int interval, string intervalUnit)
    {
        if (interval <= 0 || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return string.Empty;
        }

        return interval == 1
            ? $"every {intervalUnit}"
            : $"every {interval.ToString(System.Globalization.CultureInfo.InvariantCulture)} {intervalUnit}s";
    }
}
