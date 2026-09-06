using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the subscription lifecycle states reported by the billing system of record.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still occupies the shopper's "seat" on a plan, so subscribing
    /// again to the same plan would create a duplicate rather than a new enrollment.
    /// </summary>
    /// <remarks>
    /// Problem states such as <c>past_due</c> are treated as live on purpose: the shopper is still
    /// enrolled and the correct remedy is to fix payment, not to open a second subscription.
    /// </remarks>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "awaiting_signup",
        "on_hold",
        "suspended"
    };

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);
}
