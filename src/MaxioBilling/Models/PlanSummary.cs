namespace Microsoft.eShopWeb.MaxioBilling.Models;

/// <summary>A purchasable subscription plan (a Maxio product) in the configured product family.</summary>
public sealed record PlanSummary
{
    public int? Id { get; init; }
    public required string Handle { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in minor units (cents).</summary>
    public long? PriceInCents { get; init; }

    /// <summary>ISO currency code of the Maxio site.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit as Maxio reports it, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; init; }

    public bool HasTrial { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public long? TrialPriceInCents { get; init; }

    /// <summary>One-off setup fee in minor units, when the plan has one.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>Maxio's <c>require_credit_card</c>: a subscription cannot be created without a card.</summary>
    public bool PaymentMethodRequired { get; init; }

    /// <summary>Maxio's <c>request_credit_card</c>: a card is asked for but not enforced.</summary>
    public bool PaymentMethodRequested { get; init; }
}
