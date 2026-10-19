using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>A subscription as the billing system reports it.</summary>
/// <param name="SubscriptionId">Billing-system subscription id.</param>
/// <param name="PlanHandle">Handle of the subscribed plan (may be null when the provider omits the product).</param>
/// <param name="PlanName">Display name of the subscribed plan.</param>
/// <param name="Price">Recurring price in the site currency.</param>
/// <param name="State">Provider state, e.g. "active" (raw wire value; states unknown to this build pass through).</param>
/// <param name="NextBillingAt">When the provider next bills the subscription.</param>
/// <param name="CreatedAt">When the subscription was created.</param>
public sealed record SubscriptionSummary(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    decimal? Price,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CreatedAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this call.</summary>
    Created,

    /// <summary>The subscriber already holds a subscription to this plan; it is returned unchanged.</summary>
    AlreadySubscribed,

    /// <summary>
    /// The billing system may have created the subscription but the outcome could not be confirmed yet.
    /// It is settled on the subscriber's next subscribe or list call.
    /// </summary>
    PendingConfirmation
}

/// <summary>The result of a subscribe call.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Subscription">The subscription; null when <paramref name="Outcome"/> is <see cref="SubscribeOutcome.PendingConfirmation"/>.</param>
public sealed record SubscribeResult(SubscribeOutcome Outcome, SubscriptionSummary? Subscription);

/// <summary>The subscriber's subscriptions.</summary>
/// <param name="Subscriptions">Subscriptions the billing system holds for the subscriber.</param>
/// <param name="HasPendingSubscription">
/// True when a subscribe call for this subscriber is still unconfirmed, so a subscription may be
/// missing from <paramref name="Subscriptions"/>.
/// </param>
public sealed record MySubscriptions(IReadOnlyList<SubscriptionSummary> Subscriptions, bool HasPendingSubscription);
