using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// The subscription lifecycle states reported by the billing system.
/// </summary>
/// <remarks>
/// Values mirror the <c>SubscriptionState</c> enumeration of the Maxio Advanced Billing API
/// (https://developers.maxio.com/http/advanced-billing-api/models/enumerations/subscription-state).
/// They are kept as strings rather than a CLR enum so that a state added by the billing system
/// is surfaced verbatim instead of failing deserialization.
/// </remarks>
public static class SubscriptionStates
{
    public const string Pending = "pending";
    public const string Trialing = "trialing";
    public const string Assessing = "assessing";
    public const string Active = "active";
    public const string SoftFailure = "soft_failure";
    public const string PastDue = "past_due";
    public const string Suspended = "suspended";
    public const string Paused = "paused";
    public const string Unpaid = "unpaid";
    public const string OnHold = "on_hold";
    public const string AwaitingSignup = "awaiting_signup";
    public const string Canceled = "canceled";
    public const string Expired = "expired";
    public const string FailedToCreate = "failed_to_create";
    public const string TrialEnded = "trial_ended";

    /// <summary>
    /// States a subscription can never leave on its own. A subscription in one of these states no
    /// longer entitles the shopper to anything, so subscribing again is a new subscription rather
    /// than a duplicate of the old one.
    /// </summary>
    private static readonly HashSet<string> Terminal = new(StringComparer.OrdinalIgnoreCase)
    {
        Canceled,
        Expired,
        FailedToCreate,
        TrialEnded
    };

    public static bool IsTerminal(string? state) =>
        string.IsNullOrWhiteSpace(state) || Terminal.Contains(state!);

    /// <summary>
    /// True when the subscription still represents an ongoing relationship with the shopper -
    /// including problem states such as <c>past_due</c>, which must not be duplicated by a
    /// second signup.
    /// </summary>
    public static bool IsLive(string? state) => !IsTerminal(state);
}
