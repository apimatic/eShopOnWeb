using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Subscription lifecycle states. The set mirrors the states the billing system reports; it is kept as a
/// string so that a state added upstream does not break deserialization.
/// </summary>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Paused = "paused";
    public const string AwaitingSignup = "awaiting_signup";

    /// <summary>
    /// States in which a subscription still exists for billing purposes, so re-subscribing the same
    /// shopper to the same plan would be a duplicate rather than a new enrollment.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending, Trialing, Assessing, Active, SoftFailure, PastDue, Paused, AwaitingSignup
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
