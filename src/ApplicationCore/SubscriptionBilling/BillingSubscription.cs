using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A recurring subscription owned by a customer in the billing system of record.
/// </summary>
public class BillingSubscription
{
    /// <summary>States after which a customer may subscribe to the same plan again.</summary>
    private static readonly string[] EndOfLifeStates = ["canceled", "expired", "failed_to_create", "trial_ended"];

    public int Id { get; init; }

    /// <summary>Raw billing-system state, e.g. active, trialing, past_due, canceled.</summary>
    public string State { get; init; } = string.Empty;

    public int CustomerId { get; init; }

    /// <summary>Handle of the subscribed plan, when the billing system reports it.</summary>
    public string? PlanHandle { get; init; }

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price of the subscription in minor units (cents).</summary>
    public long PriceInCents { get; init; }

    public int Interval { get; init; } = 1;

    public string IntervalUnit { get; init; } = "month";

    /// <summary>How payment is collected: automatic, remittance, prepaid or invoice.</summary>
    public string PaymentCollectionMethod { get; init; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartsAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge/renewal is expected, if known.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>True when the subscription is in a state that occupies the plan for its customer.</summary>
    public static bool IsEndOfLife(string state) =>
        Array.Exists(EndOfLifeStates, s => string.Equals(s, state, StringComparison.OrdinalIgnoreCase));

    public bool IsEndOfLife() => IsEndOfLife(State);
}
