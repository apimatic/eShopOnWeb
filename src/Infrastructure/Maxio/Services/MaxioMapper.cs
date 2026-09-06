using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Services;

/// <summary>
/// Projects Maxio wire models onto the eShopOnWeb subscription domain.
/// </summary>
internal static class MaxioMapper
{
    private const string UnknownPlaceholder = "unknown";

    public static SubscriptionPlan ToPlan(MaxioProduct product, string currency) => new()
    {
        Id = product.Id,
        Handle = product.Handle ?? string.Empty,
        Name = product.Name ?? product.Handle ?? UnknownPlaceholder,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Currency = currency,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit ?? UnknownPlaceholder,
        RequiresPaymentMethod = product.RequireCreditCard,
        PricePointName = product.ProductPricePointName,
        TrialInterval = product.TrialInterval,
        TrialIntervalUnit = product.TrialIntervalUnit,
        TrialPriceInCents = product.TrialPriceInCents,
        ProductFamilyHandle = product.ProductFamily?.Handle
    };

    public static CustomerSubscription ToSubscription(MaxioSubscription subscription, string fallbackCurrency)
    {
        var product = subscription.Product;

        return new CustomerSubscription
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            State = ParseState(subscription.State),
            RawState = subscription.State ?? UnknownPlaceholder,
            PlanHandle = product?.Handle ?? UnknownPlaceholder,
            PlanName = product?.Name ?? product?.Handle ?? UnknownPlaceholder,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? fallbackCurrency : subscription.Currency,
            Interval = product?.Interval ?? 0,
            IntervalUnit = product?.IntervalUnit ?? UnknownPlaceholder,
            // next_assessment_at is when Maxio will actually try to collect; it tracks the period
            // end except after a failed renewal, when it holds the retry time instead.
            NextBillingAt = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CreatedAt = subscription.CreatedAt,
            BalanceInCents = subscription.BalanceInCents,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CustomerId = subscription.Customer?.Id ?? 0
        };
    }

    /// <summary>
    /// Maps a Maxio subscription state onto <see cref="SubscriptionState"/>. Unrecognised values
    /// become <see cref="SubscriptionState.Unknown"/> rather than an exception, so that a state
    /// added by the provider cannot take the endpoint down.
    /// </summary>
    public static SubscriptionState ParseState(string? state) => state?.Trim().ToLowerInvariant() switch
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
        "awaiting_signup" => SubscriptionState.AwaitingSignup,
        _ => SubscriptionState.Unknown
    };

    /// <summary>
    /// <c>true</c> when the product is offered for subscription right now.
    /// </summary>
    public static bool IsSubscribable(MaxioProduct product) =>
        !string.IsNullOrWhiteSpace(product.Handle) && product.ArchivedAt is null;

    public static DateTimeOffset SortKey(MaxioSubscription subscription) =>
        subscription.CreatedAt ?? DateTimeOffset.MinValue;
}
