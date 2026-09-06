using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the lifecycle states a subscription can be reported in.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription no longer entitles the shopper to the plan, so a fresh
    /// signup for the same plan is legitimate rather than a duplicate.
    /// </summary>
    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "unpaid",
        "trial_ended"
    };

    /// <summary>
    /// True while the subscription is still current. This deliberately includes states such as
    /// "past_due" and "suspended", where the shopper is still enrolled and must not be signed up
    /// a second time.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !Terminal.Contains(state!);
}
