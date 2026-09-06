using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Translates Maxio wire objects into the application's billing model.
/// </summary>
internal static class MaxioMappings
{
    public static SubscriptionPlan ToPlan(MaxioProduct product) => new(
        handle: product.Handle ?? string.Empty,
        name: product.Name ?? product.Handle ?? string.Empty,
        description: product.Description,
        priceInCents: product.PriceInCents,
        intervalLength: product.Interval,
        intervalUnit: product.IntervalUnit ?? "month",
        paymentMethodRequired: product.RequireCreditCard,
        pricePointHandle: product.ProductPricePointHandle,
        pricePointName: product.ProductPricePointName,
        trialIntervalLength: product.TrialInterval,
        trialIntervalUnit: product.TrialIntervalUnit,
        productFamilyHandle: product.ProductFamily?.Handle);

    public static CustomerSubscription ToSubscription(MaxioSubscription subscription) => new(
        id: subscription.Id,
        reference: subscription.Reference,
        state: subscription.State ?? "unknown",
        customerId: subscription.Customer?.Id ?? 0,
        customerReference: subscription.Customer?.Reference,
        planHandle: subscription.Product?.Handle,
        planName: subscription.Product?.Name,
        pricePointHandle: subscription.Product?.ProductPricePointHandle,
        priceInCents: subscription.ProductPriceInCents,
        intervalLength: subscription.Product?.Interval,
        intervalUnit: subscription.Product?.IntervalUnit,
        currentPeriodStartedAt: subscription.CurrentPeriodStartedAt,
        currentPeriodEndsAt: subscription.CurrentPeriodEndsAt,
        nextAssessmentAt: subscription.NextAssessmentAt,
        trialEndedAt: subscription.TrialEndedAt,
        activatedAt: subscription.ActivatedAt,
        canceledAt: subscription.CanceledAt,
        createdAt: subscription.CreatedAt,
        balanceInCents: subscription.BalanceInCents);
}
