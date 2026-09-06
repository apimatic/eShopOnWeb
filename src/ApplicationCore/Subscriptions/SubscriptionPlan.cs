namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio <c>Product</c> that lives in
/// the configured product family.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan; this is what callers pass back when subscribing.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the minor unit of <see cref="Currency"/> (e.g. cents).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO currency code of the billing site (e.g. <c>USD</c>).</summary>
    public required string Currency { get; init; }

    public required BillingInterval Interval { get; init; }

    /// <summary>Up-front, one-time charge in minor units, when the plan defines one.</summary>
    public long? SetupFeeInCents { get; init; }

    /// <summary>Length of the free/paid trial, when the plan defines one.</summary>
    public BillingInterval? Trial { get; init; }

    public long? TrialPriceInCents { get; init; }

    /// <summary>True when Maxio requires a stored payment profile before the plan can be subscribed to.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    public required bool Taxable { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public required string ProductFamilyHandle { get; init; }

    /// <summary>
    /// Maxio's numeric product id. Exposed for support/traceability only - handles are stable,
    /// numeric ids are not, so never persist or key off this value.
    /// </summary>
    public required int ProductId { get; init; }
}
