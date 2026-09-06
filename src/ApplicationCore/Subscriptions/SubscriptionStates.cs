using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription states defined by <c>Subscription-State.yaml</c> in the Maxio OpenAPI
/// specification, grouped the way that specification documents them.
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

    private static readonly string[] _endOfLife =
    {
        Canceled, Expired, FailedToCreate, TrialEnded
    };

    /// <summary>
    /// True for the spec's "End of Life" states that a subscription cannot come back from on its
    /// own, and which therefore free the plan up for a fresh signup. <c>on_hold</c> and
    /// <c>suspended</c> are deliberately excluded: the spec describes both as expected to resume.
    /// </summary>
    public static bool IsEndOfLife(string? state) =>
        state is not null && Array.Exists(_endOfLife, s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));
}
