using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// The subscription states reported by the billing system, grouped the way the docs group them:
/// live states, problem states and end-of-life states.
/// </summary>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string Trialing = "trialing";
    public const string Paused = "paused";

    public const string PastDue = "past_due";
    public const string SoftFailure = "soft_failure";
    public const string Unpaid = "unpaid";

    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string TrialEnded = "trial_ended";
    public const string OnHold = "on_hold";
    public const string Suspended = "suspended";
    public const string AwaitingSignup = "awaiting_signup";

    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Pending, Assessing, Active, Trialing, Paused
    };

    // States a subscription never comes back from. Anything else still ties the customer to the
    // plan - past_due, on_hold and suspended can all recover - so it must not be re-subscribed.
    private static readonly HashSet<string> EndedStates = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled, Expired, FailedToCreate, TrialEnded
    };

    /// <summary>True for the billing system's "live" states, i.e. the shopper is paid up and served.</summary>
    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);

    /// <summary>True once a subscription has reached an end-of-life state it cannot recover from.</summary>
    public static bool HasEnded(string? state) => state is not null && EndedStates.Contains(state);

    /// <summary>
    /// True while the subscription still represents the shopper's enrollment in the plan, including
    /// problem states. Used to keep a second subscribe request from enrolling the same shopper twice.
    /// </summary>
    public static bool IsCurrentEnrollment(string? state) => state is not null && !HasEnded(state);
}
