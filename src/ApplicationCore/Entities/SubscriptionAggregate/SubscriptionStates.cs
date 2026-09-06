using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The subscription states defined by the Maxio OpenAPI specification
/// (<c>components/schemas/Subscription-State.yaml</c>), grouped the way the spec documents them.
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
    /// States from which a subscription will never bill again. A shopper sitting in one of these
    /// is free to sign up for the same plan afresh.
    /// </summary>
    private static readonly HashSet<string> TerminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    public static bool IsTerminal(string? state) =>
        !string.IsNullOrWhiteSpace(state) && TerminalStates.Contains(state);
}
