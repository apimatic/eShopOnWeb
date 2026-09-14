using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Classifies Maxio subscription states (see Subscription States in the Billing API docs).
/// </summary>
public static class MaxioSubscriptionStates
{
    private static readonly HashSet<string> DeadStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    /// <summary>
    /// True when the subscription still occupies its plan (i.e. re-subscribing to the same
    /// plan should be treated as "already subscribed" rather than creating a duplicate).
    /// </summary>
    public static bool IsLive(string state) => !DeadStates.Contains(state);
}
