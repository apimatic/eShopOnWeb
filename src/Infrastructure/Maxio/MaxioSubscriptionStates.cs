using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Classification of Maxio's subscription states.
/// </summary>
public static class MaxioSubscriptionStates
{
    /// <summary>
    /// States a subscription can never leave. Everything else — including <c>past_due</c>,
    /// <c>on_hold</c> and <c>suspended</c> — describes a subscription that still exists and would be
    /// duplicated if we enrolled the shopper again, so the live/ended split is drawn here rather than
    /// at "is the shopper currently being served".
    /// </summary>
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    /// <summary>True when the subscription still occupies the shopper's slot on its plan.</summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndedStates.Contains(state!);
}
