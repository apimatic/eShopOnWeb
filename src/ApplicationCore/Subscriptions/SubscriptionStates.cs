using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of billing-provider subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// End-of-life states. A subscription in one of these no longer entitles the shopper to anything,
    /// so a fresh subscribe request for the same plan is a genuine new signup, not a duplicate.
    /// Every other state (including the transient <c>pending</c>/<c>assessing</c> and the problem
    /// states such as <c>past_due</c>) is treated as live.
    /// </summary>
    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    public static bool IsTerminal(string? state) => !string.IsNullOrWhiteSpace(state) && Terminal.Contains(state!);

    public static bool IsLive(string? state) => !string.IsNullOrWhiteSpace(state) && !Terminal.Contains(state!);
}
