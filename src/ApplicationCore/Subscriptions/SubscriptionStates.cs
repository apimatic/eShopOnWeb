using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the subscription states defined by the Maxio Advanced Billing
/// "Subscription State" schema.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States that represent an existing engagement between the shopper and a plan: the live and
    /// problem states, plus the pre-live <c>awaiting_signup</c>. A shopper holding a subscription in
    /// one of these states is already enrolled and must not be enrolled a second time.
    /// </summary>
    private static readonly HashSet<string> EngagedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Live states
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        // Problem states - still an active engagement, the shopper simply owes money
        "past_due",
        "soft_failure",
        "unpaid",
        // Pre-live state, transitions into a live state on its own
        "awaiting_signup"
    };

    public static bool IsEngaged(string? state) => state is not null && EngagedStates.Contains(state);
}
