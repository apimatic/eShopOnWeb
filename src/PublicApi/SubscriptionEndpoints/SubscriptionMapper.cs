using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapper
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
        BillingPeriod = plan.BillingPeriod,
        PricePointName = plan.PricePointName,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        Taxable = plan.Taxable,
        TrialInterval = plan.TrialInterval,
        TrialIntervalUnit = plan.TrialIntervalUnit,
        TrialPrice = plan.TrialPriceInCents.HasValue ? plan.TrialPriceInCents.Value / 100m : null,
        SetupFee = plan.SetupFeeInCents.HasValue ? plan.SetupFeeInCents.Value / 100m : null,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        ProductFamilyName = plan.ProductFamilyName
    };

    public static SubscriptionDto ToDto(this CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PlanPrice,
        PriceInCents = subscription.PlanPriceInCents,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PricePointName = subscription.PricePointName,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndedAt = subscription.TrialEndedAt,
        CreatedAt = subscription.CreatedAt,
        Balance = subscription.BalanceInCents / 100m,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Reference = subscription.Reference,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference,
        CustomerEmail = subscription.CustomerEmail
    };
}
