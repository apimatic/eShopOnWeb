using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription states published by the billing system, grouped by what they mean for entitlement.
/// </summary>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string FailedToCreate = "failed_to_create";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Suspended = "suspended";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string Paused = "paused";
    public const string Unpaid = "unpaid";
    public const string TrialEnded = "trial_ended";
    public const string OnHold = "on_hold";
    public const string AwaitingSignup = "awaiting_signup";

    /// <summary>
    /// States in which a subscription still occupies the shopper's slot on a plan; re-subscribing to the
    /// same plan while in one of these states must be treated as a no-op rather than a second signup.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending,
        AwaitingSignup,
        Trialing,
        Assessing,
        Active,
        SoftFailure,
        PastDue,
        Unpaid,
        Paused,
        Suspended,
        OnHold
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
