using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription in the billing system of record.
/// </summary>
public class BillingSubscription
{
    public long Id { get; init; }
    public long CustomerId { get; init; }

    /// <summary>Provider state token, e.g. active, trialing, canceled, past_due.</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public int PriceInCents { get; init; }
    public string Currency { get; init; } = string.Empty;

    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>The next date the customer will be billed (the subscription's next assessment).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public decimal Price => PriceInCents / 100m;

    public bool IsActive => SubscriptionState.IsLive(State);
}

/// <summary>
/// Classifies provider subscription state tokens. Values are confirmed against the
/// Maxio Advanced Billing "Subscription State" enumeration.
/// </summary>
public static class SubscriptionState
{
    // Live / problem states that still represent an enrolled (not-ended) subscription.
    private static readonly HashSet<string> _liveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "active", "trialing", "pending", "assessing", "past_due", "soft_failure", "unpaid", "awaiting_signup", "paused"
    };

    // End-of-life states after which a customer may (re)subscribe.
    private static readonly HashSet<string> _terminalStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled", "expired", "failed_to_create", "trial_ended", "on_hold", "suspended"
    };

    public static bool IsLive(string state) => _liveStates.Contains(state);

    public static bool IsTerminal(string state) => _terminalStates.Contains(state);
}
