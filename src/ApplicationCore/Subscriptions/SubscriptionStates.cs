using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of billing-provider subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still occupies the shopper's slot on a plan: either it is
    /// serving them today, or it is expected to resume without a new signup. Re-subscribing while one
    /// of these is in force would create a duplicate, so these are what the subscribe flow de-duplicates on.
    /// </summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "suspended",
        "paused",
        "unpaid",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);
}
