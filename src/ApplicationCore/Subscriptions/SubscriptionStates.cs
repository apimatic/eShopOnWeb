using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription states defined by the billing provider's specification.
/// Kept as strings because the provider owns the vocabulary; unknown values must not break the app.
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
    /// States a subscription can never leave without an explicit re-activation or a brand new signup.
    /// </summary>
    private static readonly HashSet<string> _terminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    /// <summary>
    /// True when the subscription still represents an existing enrolment, i.e. subscribing the same
    /// customer to the same plan again would create a duplicate. Note this is an enrolment check, not
    /// an entitlement check: dunning states such as <c>past_due</c> are "live" but not necessarily paid.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !_terminalStates.Contains(state);
}
