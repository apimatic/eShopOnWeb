namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio Advanced Billing
/// <c>Product</c> (see <c>Product.yaml</c> in the Maxio OpenAPI specification) that belongs to the
/// configured product family.
/// </summary>
public sealed class SubscriptionPlan
{
    /// <summary>Maxio product id. Unstable across re-seeds; prefer <see cref="Handle"/>.</summary>
    public required long Id { get; init; }

    /// <summary>Maxio product API handle - the stable identifier callers subscribe with.</summary>
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in the minor unit of <see cref="Currency"/>.</summary>
    public required long PriceInCents { get; init; }

    /// <summary>ISO-4217 code of the Maxio site currency.</summary>
    public required string Currency { get; init; }

    /// <summary>Number of <see cref="IntervalUnit"/>s between renewals (e.g. 1 with "month").</summary>
    public required int Interval { get; init; }

    /// <summary>Maxio interval unit, one of "month" or "day".</summary>
    public required string IntervalUnit { get; init; }

    public string? ProductFamilyHandle { get; init; }

    /// <summary>Maxio <c>require_credit_card</c>: a payment profile must exist before signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public long? SetupFeeInCents { get; init; }

    public int? TrialInterval { get; init; }

    public string? TrialIntervalUnit { get; init; }

    public long? TrialPriceInCents { get; init; }

    public bool HasTrial => TrialInterval is > 0;
}
