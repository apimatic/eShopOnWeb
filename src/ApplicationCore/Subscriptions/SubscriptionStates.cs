using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Helpers for reasoning about Maxio subscription states. The set of states comes
/// from the Maxio OpenAPI spec (Subscription-State enum).
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// End-of-life states in which a subscription is effectively dead and a shopper
    /// could legitimately re-subscribe. Every other state is treated as a live
    /// enrollment so that a repeated subscribe call is idempotent rather than
    /// creating a duplicate.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended",
    };

    /// <summary>
    /// True when the subscription is not in an end-of-life state and therefore counts
    /// as an active enrollment.
    /// </summary>
    public static bool IsLive(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return false;
        }

        return !TerminalStates.Contains(state);
    }
}
