using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>
/// The values of the specification's <c>Subscription State</c> enum, grouped the way the specification
/// documents them (Live / Problem / End of Life states).
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

    /// <summary>States from which a subscription will not come back on its own.</summary>
    private static readonly HashSet<string> EndOfLifeStates = new(StringComparer.OrdinalIgnoreCase)
    {
        FailedToCreate, Suspended, Canceled, Expired, TrialEnded, OnHold
    };

    /// <summary>
    /// True for any state that is not an End of Life state — i.e. the subscription is still in force
    /// (including the Problem states, which are being dunned rather than terminated).
    /// A shopper who already has a live subscription to a plan must not be enrolled a second time.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLifeStates.Contains(state!);
}
