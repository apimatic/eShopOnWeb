using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the billing provider's subscription lifecycle states.
/// </summary>
/// <remarks>
/// States and their meaning are taken from the Maxio Advanced Billing subscription state reference.
/// A state that is neither live nor terminal is treated as live, so an unknown future state never
/// causes a duplicate enrollment.
/// </remarks>
public static class SubscriptionStates
{
    /// <summary>States in which the subscription is finished and re-subscribing is legitimate.</summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    public static bool IsTerminal(string? state) =>
        !string.IsNullOrWhiteSpace(state) && TerminalStates.Contains(state);

    public static bool IsLive(string? state) => !IsTerminal(state);
}
