using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Subscription lifecycle states, grouped the way the billing system documents them.
/// Used to decide whether an existing subscription should be reused instead of creating a new one.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>States in which the shopper is enrolled and being billed normally.</summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "assessing", "pending", "trialing", "paused", "awaiting_signup"
    };

    /// <summary>
    /// States in which billing has hit a problem but the shopper is still enrolled - a re-subscribe
    /// here would create a second, duplicate enrollment rather than fixing the problem.
    /// </summary>
    private static readonly HashSet<string> Problem = new(StringComparer.OrdinalIgnoreCase)
    {
        "past_due", "soft_failure", "unpaid"
    };

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);

    public static bool IsProblem(string? state) => state is not null && Problem.Contains(state);

    /// <summary>
    /// True when an existing subscription in this state should be handed back to the caller rather
    /// than superseded by a new one. Anything else (canceled, expired, failed_to_create, ...) is
    /// end of life and a fresh subscription is the correct response.
    /// </summary>
    public static bool BlocksResubscribe(string? state) => IsLive(state) || IsProblem(state);
}
