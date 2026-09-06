using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The subscription states Maxio Advanced Billing reports, and which of them still
/// represent an enrollment the shopper holds.
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
    /// States in which a subscription no longer entitles the shopper to the plan, so a
    /// fresh subscription to the same plan is a legitimate new enrollment. Anything else
    /// counts as live, which makes an unrecognised future state fail safe: we return the
    /// existing subscription rather than silently enrolling the user a second time.
    /// </summary>
    private static readonly HashSet<string> TerminalStates =
        new(StringComparer.OrdinalIgnoreCase) { Canceled, Expired, FailedToCreate };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state!);
}
