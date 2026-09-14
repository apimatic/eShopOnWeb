using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of billing-provider subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States a subscription can never come back from. Everything else - including the recoverable
    /// "problem" states (past_due, unpaid, soft_failure) and the paused-but-resumable states
    /// (on_hold, suspended) - counts as live, so we never bill a shopper twice for the same plan
    /// just because their current subscription is temporarily unhealthy.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    public static bool IsTerminal(string? state) =>
        !string.IsNullOrWhiteSpace(state) && TerminalStates.Contains(state!);

    public static bool IsLive(string? state) => !IsTerminal(state);
}
