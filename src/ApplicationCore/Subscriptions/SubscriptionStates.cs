using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the raw subscription state strings reported by the billing provider.
/// The provider owns the vocabulary, so unknown values are treated conservatively as "live"
/// rather than silently letting a shopper enroll twice on the same plan.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States a subscription can never come back from. A shopper holding only subscriptions in these
    /// states is free to subscribe to the plan again.
    /// </summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended",
    };

    /// <summary>States in which the shopper is entitled to the plan and is not in a problem state.</summary>
    private static readonly HashSet<string> HealthyStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
    };

    /// <summary>
    /// True when the subscription still occupies the shopper's seat on the plan, i.e. subscribing again
    /// would create a duplicate. Problem states (past_due, unpaid, on_hold, ...) count as live.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state);

    /// <summary>True when the shopper currently has access to whatever the plan entitles them to.</summary>
    public static bool IsHealthy(string? state) =>
        !string.IsNullOrWhiteSpace(state) && HealthyStates.Contains(state);
}
