using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Projects subscription domain models onto the API contract. Written out by hand rather than
/// configured by convention, so that a change to either side is visible in review.
/// </summary>
internal static class SubscriptionMapper
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.Price,
        Currency = plan.Currency,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = DescribePeriod(plan.Interval, plan.IntervalUnit) ?? string.Empty,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        HasTrial = plan.HasTrial,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        TrialPriceInCents = plan.TrialPriceInCents,
        SetupFeeInCents = plan.InitialChargeInCents,
        Taxable = plan.Taxable,
        PricePointName = plan.PricePointName,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        ProductFamilyName = plan.ProductFamilyName
    };

    public static SubscriptionDto ToDto(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.RawState ?? subscription.State.ToString().ToLowerInvariant(),
        IsCurrent = subscription.IsCurrent,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.ProductPriceInCents,
        Price = subscription.ProductPrice,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingPeriod = DescribePeriod(subscription.Interval, subscription.IntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        TrialStartedAt = subscription.TrialStartedAt,
        TrialEndedAt = subscription.TrialEndedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        ExpiresAt = subscription.ExpiresAt,
        CreatedAt = subscription.CreatedAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };

    private static string? DescribePeriod(int? interval, string? intervalUnit)
    {
        if (interval is null || string.IsNullOrWhiteSpace(intervalUnit))
        {
            return null;
        }

        return interval == 1
            ? $"every {intervalUnit}"
            : string.Format(CultureInfo.InvariantCulture, "every {0} {1}s", interval, intervalUnit);
    }
}
