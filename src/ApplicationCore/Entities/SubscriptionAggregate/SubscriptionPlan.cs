namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from the billing provider's
/// catalog; eShopOnWeb never stores plan pricing itself - the billing provider is the
/// system of record.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable, human readable identifier of the plan (never the numeric id, which is not stable).</summary>
    public required string Handle { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price expressed in the minor unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price expressed in the major unit (dollars).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Numerical billing interval, coupled with <see cref="IntervalUnit"/> (e.g. 1 "month").</summary>
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the billing provider requires a stored payment method before the plan can be started.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Length of the free trial, expressed with <see cref="TrialIntervalUnit"/>; null when the plan has no trial.</summary>
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }

    /// <summary>Price charged during the trial period, in the minor unit; null when the plan has no trial.</summary>
    public long? TrialPriceInCents { get; init; }
}
