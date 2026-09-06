using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Billing.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects the billing models onto the public API DTOs. Kept as an explicit mapper rather than an
/// AutoMapper profile because the computed <c>BillingPeriod</c> text and the
/// <c>IsLive</c>-to-<c>IsActive</c> rename are easier to read (and to test) written out.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        PriceInCents = plan.PriceInCents,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = DescribePeriod(plan.Interval, plan.IntervalUnit),
        ProductFamilyHandle = plan.ProductFamilyHandle,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        SetupFeeInCents = plan.SetupFeeInCents,
        Taxable = plan.Taxable
    };

    public static SubscriptionDto ToDto(SubscriberSubscription subscription) => new()
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
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    /// <summary>Renders "1 / month" as "every month" and "3 / month" as "every 3 months".</summary>
    internal static string DescribePeriod(int interval, string? intervalUnit)
    {
        if (string.IsNullOrWhiteSpace(intervalUnit) || interval <= 0)
        {
            return string.Empty;
        }

        return interval == 1
            ? $"every {intervalUnit}"
            : string.Create(CultureInfo.InvariantCulture, $"every {interval} {intervalUnit}s");
    }
}
