using System;
using MaxioAdvancedBilling.Models;
using MaxioAdvancedBilling.Models.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>Maps Maxio SDK models onto the provider-neutral billing types.</summary>
internal static class MaxioMapping
{
    public static SubscriptionPlan ToPlan(Product product) => new(
        Handle: product.Handle!,
        Name: string.IsNullOrWhiteSpace(product.Name) ? product.Handle! : product.Name,
        Description: string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents: product.PriceInCents,
        Interval: product.Interval,
        // Open enums: .Value is the wire value, including values this SDK version does not declare.
        IntervalUnit: product.IntervalUnit?.Value,
        InitialChargeInCents: product.InitialChargeInCents,
        TrialPriceInCents: product.TrialPriceInCents,
        TrialInterval: product.TrialInterval,
        TrialIntervalUnit: product.TrialIntervalUnit?.Value);

    public static SubscriptionSummary ToSummary(Subscription subscription, string? fallbackPlanHandle = null)
    {
        // Under Maxio's newer catalog the embedded product may be null; tolerate it.
        var product = subscription.Product;
        return new SubscriptionSummary(
            Id: subscription.Id ?? 0,
            Reference: subscription.Reference,
            PlanHandle: product?.Handle ?? fallbackPlanHandle,
            PlanName: product?.Name,
            State: subscription.State?.Value ?? "unknown",
            IsLive: IsLive(subscription.State),
            PriceInCents: subscription.ProductPriceInCents ?? product?.PriceInCents,
            Currency: subscription.Currency,
            Interval: product?.Interval,
            IntervalUnit: product?.IntervalUnit?.Value,
            NextBillingAt: subscription.CurrentPeriodEndsAt,
            NextAssessmentAt: subscription.NextAssessmentAt,
            ActivatedAt: subscription.ActivatedAt,
            CreatedAt: subscription.CreatedAt);
    }

    /// <summary>
    /// A subscription is live unless it reached a terminal state. Unknown or missing states count as
    /// live, so a state this SDK version does not know never lets a shopper double-subscribe.
    /// </summary>
    public static bool IsLive(SubscriptionState? state) =>
        state is null
        || !(state == SubscriptionState.Canceled
             || state == SubscriptionState.Expired
             || state == SubscriptionState.FailedToCreate);

    public static bool HasPlan(Subscription subscription, string planHandle, string? fallbackPlanHandle) =>
        string.Equals(subscription.Product?.Handle ?? fallbackPlanHandle, planHandle, StringComparison.Ordinal);
}
