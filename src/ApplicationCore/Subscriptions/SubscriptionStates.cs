using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies billing-system subscription states into "still entitles the shopper" versus
/// "end of life". Used to decide whether a repeat subscribe request should be treated as a
/// duplicate of an existing enrollment or as a genuine re-subscribe.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription is still running: the shopper is enrolled and a further
    /// subscribe request for the same plan is a duplicate, not a new enrollment.
    /// </summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);
}
