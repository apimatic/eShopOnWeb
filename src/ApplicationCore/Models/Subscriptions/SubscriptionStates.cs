using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// Classification of billing-system subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still belongs to the shopper: it either entitles them to the
    /// service or is expected to, once billing catches up. Re-subscribing to the same plan while a
    /// subscription is in one of these states is a no-op rather than a second enrollment.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
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

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
