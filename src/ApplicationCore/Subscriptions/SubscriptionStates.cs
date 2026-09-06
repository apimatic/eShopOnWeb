using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription lifecycle states the billing system reports, and the one classification this
/// integration needs: whether a subscription still represents a real enrollment.
/// </summary>
/// <remarks>
/// State names and their grouping follow the Maxio Advanced Billing subscription-state taxonomy.
/// States are kept as strings rather than an enum so that a state added by the provider surfaces
/// verbatim instead of failing deserialization.
/// </remarks>
public static class SubscriptionStates
{
    public const string Active = "active";
    public const string Assessing = "assessing";
    public const string AwaitingSignup = "awaiting_signup";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string OnHold = "on_hold";
    public const string PastDue = "past_due";
    public const string Paused = "paused";
    public const string Pending = "pending";
    public const string SoftFailure = "soft_failure";
    public const string Suspended = "suspended";
    public const string Trialing = "trialing";
    public const string TrialEnded = "trial_ended";
    public const string Unpaid = "unpaid";

    /// <summary>
    /// States in which a subscription is finished for good. A subscriber in one of these states is
    /// free to enrol again; every other state — including problem states such as <c>past_due</c> and
    /// paused states such as <c>on_hold</c> — is still a live enrollment that must not be duplicated.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    /// <summary>Whether the state means the subscription has reached the end of its life.</summary>
    public static bool IsTerminal(string? state) => state is not null && TerminalStates.Contains(state);

    /// <summary>Whether the state means the subscriber is still enrolled.</summary>
    public static bool IsLiveEnrollment(string? state) => !IsTerminal(state);
}
