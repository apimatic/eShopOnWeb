using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the subscription states defined by the billing system.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription still occupies the shopper's slot for a plan: the shopper is
    /// enrolled, or is in the middle of being enrolled, or is enrolled but behind on payment. A
    /// second subscribe request while one of these is outstanding is a duplicate, not a new
    /// enrollment. End-of-life states (canceled, expired, trial_ended, failed_to_create, on_hold)
    /// are deliberately absent so a shopper can re-subscribe after cancelling.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "assessing",
        "trialing",
        "active",
        "soft_failure",
        "past_due",
        "unpaid",
        "paused",
        "suspended",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
