using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the billing system's subscription states. The set mirrors the states
/// documented by Maxio Advanced Billing. Unrecognised values are treated as neither live nor
/// entitled, so a state introduced upstream can never silently grant access.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which the subscriber is still enrolled and the subscription still occupies the
    /// plan, so a second signup for the same plan would be a duplicate.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Live
        "active", "assessing", "pending", "trialing", "paused", "awaiting_signup",
        // Problem states - enrolled, but not paid up
        "past_due", "soft_failure", "unpaid",
        // Temporarily stopped, expected to resume
        "on_hold", "suspended",
    };

    /// <summary>States in which the shopper should have access to the paid service.</summary>
    private static readonly HashSet<string> EntitledStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "assessing", "trialing", "past_due", "soft_failure",
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);

    public static bool GrantsEntitlement(string? state) => state is not null && EntitledStates.Contains(state);
}
