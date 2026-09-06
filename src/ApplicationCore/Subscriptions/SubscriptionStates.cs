using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies the subscription lifecycle states reported by the billing system.
/// </summary>
/// <remarks>
/// States are enumerated from the terminal end so that any state we do not recognise -- including
/// ones the billing system adds later -- is treated as live. Treating an unknown state as live is
/// the safe default: it makes a repeated subscribe attempt return the existing subscription rather
/// than silently enrolling the shopper a second time.
/// </remarks>
public static class SubscriptionStates
{
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create"
    };

    public static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    public static bool IsLive(string? state) => !IsTerminal(state);
}
