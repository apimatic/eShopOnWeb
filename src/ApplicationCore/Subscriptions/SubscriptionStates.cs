using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies billing-system subscription states into "still a going concern" and "end of life".
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// Live and problem states. The subscription still exists for the customer, so a repeated
    /// subscribe request must resolve to it instead of enrolling the shopper twice.
    /// </summary>
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "past_due",
        "soft_failure",
        "unpaid",
        "suspended",
        "on_hold",
        "awaiting_signup"
    };

    // Everything else — canceled, expired, failed_to_create, trial_ended — is end of life, and a
    // shopper is allowed to start a fresh subscription on the same plan.

    public static bool IsLive(string? state) => state is not null && Live.Contains(state);
}
