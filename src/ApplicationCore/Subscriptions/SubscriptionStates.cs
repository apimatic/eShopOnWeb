using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription states reported by the billing system, and the rules used to decide
/// whether an existing subscription still counts as the shopper's current enrollment.
/// </summary>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string AwaitingSignup = "awaiting_signup";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Suspended = "suspended";
    public const string Paused = "paused";
    public const string Unpaid = "unpaid";
    public const string OnHold = "on_hold";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string TrialEnded = "trial_ended";

    /// <summary>
    /// States a subscription can never come back from. A shopper whose only subscription to a plan
    /// is in one of these states is free to subscribe to that plan again.
    /// </summary>
    private static readonly HashSet<string> _terminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled, Expired, FailedToCreate, TrialEnded
    };

    /// <summary>
    /// States in which the shopper is receiving the service they pay for.
    /// </summary>
    private static readonly HashSet<string> _healthyStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Active, Trialing, Assessing, Pending
    };

    public static bool IsTerminal(string? state) => state is not null && _terminalStates.Contains(state);

    /// <summary>
    /// True when the subscription still ties the shopper to the plan - including problem states such as
    /// <c>past_due</c>, which must not be treated as an invitation to create a second subscription.
    /// </summary>
    public static bool IsCurrent(string? state) => !string.IsNullOrWhiteSpace(state) && !IsTerminal(state);

    public static bool IsHealthy(string? state) => state is not null && _healthyStates.Contains(state);
}
