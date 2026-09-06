namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
/// <remarks>
/// Projected from the Maxio <c>Product</c> schema
/// (<c>maxio-spec/components/schemas/Product.yaml</c>). The plan <see cref="Handle"/> is the stable
/// identifier; Maxio numeric ids are reassigned when a catalog is re-seeded and are therefore never
/// used as the contract between eShopOnWeb and its callers.
/// </remarks>
public record SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (Maxio <c>product.handle</c>).</summary>
    public required string Handle { get; init; }

    /// <summary>Provider identifier of the plan (Maxio <c>product.id</c>). Informational only.</summary>
    public required int Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (Maxio <c>product.price_in_cents</c>).</summary>
    public required long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount, derived from <see cref="PriceInCents"/>.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code of the Maxio site the plan belongs to (Maxio <c>site.currency</c>).</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (Maxio <c>product.interval</c>).</summary>
    public required int Interval { get; init; }

    /// <summary>Renewal interval unit, e.g. month or day (Maxio <c>product.interval_unit</c>).</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Whether a payment profile must be captured before a subscription can be created.</summary>
    public required bool RequiresPaymentMethod { get; init; }

    /// <summary>Name of the price point the plan is currently priced from.</summary>
    public string? PricePointName { get; init; }

    /// <summary>Length of the trial period, when the plan has one.</summary>
    public int? TrialInterval { get; init; }

    /// <summary>Unit of <see cref="TrialInterval"/>, when the plan has a trial.</summary>
    public string? TrialIntervalUnit { get; init; }

    /// <summary>Trial price in the smallest currency unit, when the plan has a trial.</summary>
    public long? TrialPriceInCents { get; init; }

    /// <summary>Handle of the product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
