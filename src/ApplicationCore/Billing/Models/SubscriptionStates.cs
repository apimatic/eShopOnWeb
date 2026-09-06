using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing.Models;

/// <summary>
/// Classification of the subscription states enumerated by the <c>Subscription-State</c> schema
/// of the Maxio specification.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States the specification groups under "Live States", plus <c>awaiting_signup</c>, which is a
    /// scheduled-but-not-yet-billing subscription. A subscriber in any of these already occupies a
    /// slot on the plan.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "assessing",
        "pending",
        "trialing",
        "paused",
        "awaiting_signup"
    };

    /// <summary>
    /// The "Problem States" of the specification. Payment has failed but the enrollment still
    /// exists, so these also block a duplicate enrollment.
    /// </summary>
    private static readonly HashSet<string> ProblemStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "past_due",
        "soft_failure",
        "unpaid"
    };

    /// <summary>
    /// True for a state that represents an enrollment that has not ended. This is what makes
    /// subscribe idempotent: a shopper already enrolled on a plan gets the existing subscription
    /// back instead of a second one.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && (LiveStates.Contains(state) || ProblemStates.Contains(state));
}
