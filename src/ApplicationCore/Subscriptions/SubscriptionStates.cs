using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of the billing provider's subscription states.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States a subscription can never leave. Anything else is treated as live, so an
    /// enrolment attempt that would duplicate a still-running subscription is rejected.
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
}
