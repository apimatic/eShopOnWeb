namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// Projected from a product in the configured Maxio Advanced Billing product family.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>
    /// Stable, human readable identifier of the plan (Maxio product handle).
    /// Handles - not numeric ids - are the contract with callers, because Maxio
    /// reassigns numeric ids whenever a catalog is re-seeded.
    /// </summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price of the plan, in the smallest unit of <see cref="Currency"/>.</summary>
    public int PriceInCents { get; init; }

    /// <summary>Recurring price of the plan as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code of the Maxio site the plan lives on.</summary>
    public string? Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; init; }

    /// <summary>Billing period unit, e.g. "month" or "day".</summary>
    public string? IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>
    /// True when Maxio requires a payment profile before the subscription can be created.
    /// Plans that require one cannot be subscribed to through this API, because card
    /// capture (Chargify.js / 3-DS) is out of scope for this integration.
    /// </summary>
    public bool RequiresPaymentMethod { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }
}
