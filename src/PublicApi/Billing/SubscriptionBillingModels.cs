using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>A plan the shopper can subscribe to (a Maxio product in the configured product family).</summary>
public sealed record PlanView(
    string Handle,
    string? Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <param name="Truncated">True when the plan list hit its page cap and may be incomplete.</param>
public sealed record PlanCatalog(IReadOnlyList<PlanView> Plans, bool Truncated);

/// <summary>A Maxio subscription as shown to the shopper.</summary>
public sealed record SubscriptionView(
    long Id,
    string? Reference,
    string State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);

public enum SubscribeStatus
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The account already had this subscription (double-click, retry); nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>
    /// The subscription request reached Maxio but its outcome could not be confirmed yet. It is settled
    /// automatically on the next subscribe or my-subscriptions call; it is never created twice.
    /// </summary>
    PendingConfirmation
}

public sealed record SubscribeResult(SubscribeStatus Status, SubscriptionView? Subscription);
