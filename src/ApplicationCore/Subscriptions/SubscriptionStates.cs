using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Well known subscription states reported by the billing system, and the rules this
/// application uses to decide whether a subscription still "counts" for a subscriber.
/// </summary>
public static class SubscriptionStates
{
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string Pending = "pending";
    public const string AwaitingSignup = "awaiting_signup";
    public const string PastDue = "past_due";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string TrialEnded = "trial_ended";

    /// <summary>
    /// States in which a subscription is finished for good. Anything else is treated as
    /// still-in-force so that a shopper is never enrolled twice (and billed twice) for the
    /// same plan while an earlier subscription is only temporarily unhealthy.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !TerminalStates.Contains(state);
}
