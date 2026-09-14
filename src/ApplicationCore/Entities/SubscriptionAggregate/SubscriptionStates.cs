using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Subscription lifecycle states. The values mirror the billing provider's documented
/// subscription states; only the grouping (live vs. end-of-life) is ours.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still exists for the shopper, so a repeated
    /// subscribe attempt must be treated as a duplicate rather than a new enrollment.
    /// Problem states (past_due, unpaid, ...) are deliberately included: the shopper is
    /// still enrolled, they simply owe money.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
