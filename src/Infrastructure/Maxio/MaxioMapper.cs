using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Translates Maxio wire contracts into the provider-neutral subscription model.
/// </summary>
internal static class MaxioMapper
{
    public static SubscriptionPlan ToPlan(MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? string.Empty,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? string.Empty,
        TrialPriceInCents = product.TrialPriceInCents,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        InitialChargeInCents = product.InitialChargeInCents,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable,
        ProductFamilyHandle = product.ProductFamily?.Handle,
        ProductFamilyName = product.ProductFamily?.Name,
        PricePointHandle = product.ProductPricePointHandle,
        PricePointName = product.ProductPricePointName,
        UpdatedAt = product.UpdatedAt
    };

    public static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? "unknown",
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        PriceInCents = subscription.ProductPriceInCents,
        Interval = subscription.Product?.Interval,
        IntervalUnit = subscription.Product?.IntervalUnit,
        Currency = subscription.Currency,

        // next_assessment_at is when payment will next be captured; it tracks the end of the current
        // period except while a failed payment is being retried, so fall back to the period end.
        NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        TrialStartedAt = subscription.TrialStartedAt,
        TrialEndedAt = subscription.TrialEndedAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod ?? false,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Reference = subscription.Reference,
        CustomerId = subscription.Customer?.Id,
        CustomerReference = subscription.Customer?.Reference
    };
}
