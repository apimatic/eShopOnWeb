using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// The subscription states declared by the specification schema <c>Subscription-State</c>, grouped
/// the way the specification documents them (live / problem / end-of-life).
/// </summary>
public static class MaxioSubscriptionStates
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

    /// <summary>
    /// States a subscription cannot come back from without an explicit reactivation. A subscription
    /// in one of these states no longer occupies the shopper's slot on that plan, so subscribing
    /// again must create a new subscription.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    /// <summary>
    /// True when the subscription is still an ongoing billing relationship. Includes the problem
    /// states (<c>past_due</c>, <c>unpaid</c>, ...) and temporary stops (<c>on_hold</c>,
    /// <c>suspended</c>), because those must not be duplicated by a second subscribe.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state.Trim());
}
