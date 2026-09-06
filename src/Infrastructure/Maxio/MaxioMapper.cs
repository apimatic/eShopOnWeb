using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Projects Maxio API payloads onto the application's own subscription model.</summary>
internal static class MaxioMapper
{
    public static SubscriptionPlan ToPlan(MaxioProduct product, string currency) => new()
    {
        Handle = product.Handle!,
        Name = product.Name ?? product.Handle!,
        Description = string.IsNullOrWhiteSpace(product.Description) ? null : product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = new BillingInterval(product.Interval, product.IntervalUnit ?? "month"),
        SetupFeeInCents = product.InitialChargeInCents > 0 ? product.InitialChargeInCents : null,
        Trial = product.TrialInterval is > 0
            ? new BillingInterval(product.TrialInterval.Value, product.TrialIntervalUnit ?? "day")
            : null,
        TrialPriceInCents = product.TrialInterval is > 0 ? product.TrialPriceInCents : null,
        RequiresPaymentMethod = product.RequireCreditCard,
        Taxable = product.Taxable,
        ProductFamilyHandle = product.ProductFamily?.Handle ?? string.Empty,
        ProductId = product.Id
    };

    public static Subscription ToSubscription(MaxioSubscription subscription, string fallbackCurrency)
    {
        var product = subscription.Product;

        return new Subscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = ParseState(subscription.State),
            RawState = subscription.State ?? "unknown",
            PlanHandle = product?.Handle ?? string.Empty,
            PlanName = product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? fallbackCurrency : subscription.Currency!,
            Interval = new BillingInterval(product?.Interval ?? 0, product?.IntervalUnit ?? "month"),
            NextBillingAt = subscription.NextAssessmentAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            TrialEndsAt = subscription.TrialEndedAt,
            CreatedAt = subscription.CreatedAt ?? DateTimeOffset.MinValue,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CustomerId = subscription.Customer?.Id ?? 0,
            CustomerReference = subscription.Customer?.Reference
        };
    }

    /// <summary>
    /// Maps the snake_case values of the <c>Subscription-State</c> enum onto
    /// <see cref="SubscriptionState"/>, falling back to <see cref="SubscriptionState.Unknown"/> so a
    /// state added by Maxio later cannot break the endpoint.
    /// </summary>
    public static SubscriptionState ParseState(string? state) => state switch
    {
        "pending" => SubscriptionState.Pending,
        "failed_to_create" => SubscriptionState.FailedToCreate,
        "trialing" => SubscriptionState.Trialing,
        "assessing" => SubscriptionState.Assessing,
        "active" => SubscriptionState.Active,
        "soft_failure" => SubscriptionState.SoftFailure,
        "past_due" => SubscriptionState.PastDue,
        "suspended" => SubscriptionState.Suspended,
        "canceled" => SubscriptionState.Canceled,
        "expired" => SubscriptionState.Expired,
        "paused" => SubscriptionState.Paused,
        "unpaid" => SubscriptionState.Unpaid,
        "trial_ended" => SubscriptionState.TrialEnded,
        "on_hold" => SubscriptionState.OnHold,
        _ => SubscriptionState.Unknown
    };
}
