using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies billing-system subscription states into "still entitles the shopper" versus "ended".
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription is considered to still exist for the shopper, so a repeat
    /// subscribe attempt returns the existing subscription rather than creating a second one.
    /// End-of-life states (canceled, expired, trial_ended, failed_to_create, on_hold, suspended)
    /// are deliberately absent: a shopper whose subscription ended is allowed to subscribe again.
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

    public static bool IsLive(string? state) => !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state!);
}
