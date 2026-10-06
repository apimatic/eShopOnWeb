using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Lifecycle state of a subscription as reported by the billing system of record.
/// The values mirror the <c>Subscription-State</c> enum of the Maxio Advanced Billing
/// OpenAPI specification (maxio-spec/openapi.yaml -> components/schemas/Subscription-State.yaml).
/// <see cref="Unknown"/> keeps the app forward compatible if Maxio adds a state.
/// </summary>
public enum SubscriptionState
{
    Unknown = 0,
    Pending,
    FailedToCreate,
    Trialing,
    Assessing,
    Active,
    SoftFailure,
    PastDue,
    Suspended,
    Canceled,
    Expired,
    Paused,
    Unpaid,
    TrialEnded,
    OnHold,
    AwaitingSignup
}

public static class SubscriptionStateExtensions
{
    private static readonly Dictionary<string, SubscriptionState> _specValues = new(StringComparer.OrdinalIgnoreCase)
    {
        { "pending", SubscriptionState.Pending },
        { "failed_to_create", SubscriptionState.FailedToCreate },
        { "trialing", SubscriptionState.Trialing },
        { "assessing", SubscriptionState.Assessing },
        { "active", SubscriptionState.Active },
        { "soft_failure", SubscriptionState.SoftFailure },
        { "past_due", SubscriptionState.PastDue },
        { "suspended", SubscriptionState.Suspended },
        { "canceled", SubscriptionState.Canceled },
        { "expired", SubscriptionState.Expired },
        { "paused", SubscriptionState.Paused },
        { "unpaid", SubscriptionState.Unpaid },
        { "trial_ended", SubscriptionState.TrialEnded },
        { "on_hold", SubscriptionState.OnHold },
        { "awaiting_signup", SubscriptionState.AwaitingSignup }
    };

    public static SubscriptionState Parse(string? value)
        => value is not null && _specValues.TryGetValue(value, out var state) ? state : SubscriptionState.Unknown;

    /// <summary>
    /// True when the subscription can no longer bill the shopper (Maxio "End of Life" states).
    /// A subscription in one of these states never blocks a new enrollment for the same plan.
    /// </summary>
    public static bool IsTerminal(this SubscriptionState state) => state switch
    {
        SubscriptionState.Canceled => true,
        SubscriptionState.Expired => true,
        SubscriptionState.FailedToCreate => true,
        SubscriptionState.TrialEnded => true,
        _ => false
    };

    /// <summary>True while the enrollment occupies the shopper's plan slot.</summary>
    public static bool IsActive(this SubscriptionState state) => !state.IsTerminal() && state != SubscriptionState.Unknown;

    public static string? ToSpecValue(this SubscriptionState state)
    {
        foreach (var pair in _specValues)
        {
            if (pair.Value == state) return pair.Key;
        }
        return null;
    }
}
