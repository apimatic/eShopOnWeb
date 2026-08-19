using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public static class BillingState
{
    /// <summary>
    /// Maxio live + problem states where the shopper already has a working subscription.
    /// End-of-life states (canceled, expired, …) are excluded so they can re-enroll.
    /// Spec: Subscription-State.yaml
    /// </summary>
    private static readonly HashSet<string> LiveOrProblemStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid"
    };

    public static bool IsExistingEnrollment(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveOrProblemStates.Contains(state);
}
