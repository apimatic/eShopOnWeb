using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies billing-provider subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States from which a subscription will never bill again. Anything else — including problem
    /// states such as <c>past_due</c> — still occupies the shopper's slot on that plan, so a second
    /// subscribe attempt must return the existing subscription instead of creating another one.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);

    public static bool IsTerminal(string? state) => !IsLive(state);
}
