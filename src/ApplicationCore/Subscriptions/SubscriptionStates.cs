using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the Maxio subscription states eShopOnWeb cares about.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still occupies the customer's slot on a plan, so subscribing
    /// again to the same plan would create a duplicate rather than a new enrollment. Covers Maxio's
    /// "Live" states plus the problem states, which are recoverable (a failed payment does not end
    /// the subscription).
    /// </summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "assessing",
        "trialing",
        "active",
        "paused",
        "awaiting_signup",
        "past_due",
        "soft_failure",
        "unpaid"
    };

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);
}
