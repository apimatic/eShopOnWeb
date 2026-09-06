namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to, projected from the billing system of record.
/// </summary>
/// <remarks>
/// <see cref="Handle"/> is the stable identifier callers should use when subscribing. Numeric
/// provider ids are deliberately not surfaced: they are reassigned whenever the catalog is re-seeded.
/// </remarks>
public class SubscriptionPlan
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO 4217 currency code of the billing site.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with "month").</summary>
    public int Interval { get; init; }

    /// <summary>Renewal interval unit, "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }

    /// <summary>True when the plan has a trial period configured.</summary>
    public bool HasTrial { get; init; }

    /// <summary>Length of the trial period, expressed in <see cref="TrialIntervalUnit"/>s.</summary>
    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    /// <summary>One-off charge applied at signup, in the smallest currency unit.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>True when the provider requires a stored payment method before a signup can succeed.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
