using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The billing provider's subscription state vocabulary, grouped the way this application
/// reasons about it. See the provider's "Subscription States" documentation for the full list.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which the shopper is still entitled to the product: the provider's documented
    /// Live States, plus <c>past_due</c> (access is retained while dunning runs).
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "awaiting_signup",
        "past_due"
    };

    /// <summary>
    /// States that still occupy the customer/plan slot. Re-subscribing to the same plan while a
    /// subscription is in one of these states must be a no-op, not a second signup. This is the
    /// live set plus the states a subscription can recover from without a new signup.
    /// </summary>
    private static readonly HashSet<string> OccupyingStates = new(LiveStates, StringComparer.OrdinalIgnoreCase)
    {
        "soft_failure",
        "unpaid",
        "on_hold",
        "suspended"
    };

    /// <summary>True while the subscription entitles the shopper to the product.</summary>
    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);

    /// <summary>
    /// True when an existing subscription in this state should be returned instead of creating
    /// another one. States outside this set (<c>canceled</c>, <c>expired</c>, <c>trial_ended</c>,
    /// <c>failed_to_create</c>) are terminal, so the shopper is free to sign up again.
    /// </summary>
    public static bool OccupiesPlanSlot(string? state) => state is not null && OccupyingStates.Contains(state);
}
