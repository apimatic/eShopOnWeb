namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. This is the eShopOnWeb view of a
/// Maxio Advanced Billing "product" that belongs to the configured product family.
/// Prices are expressed in integer cents (the unit Maxio uses on the wire) so that no
/// precision is lost; <see cref="PriceInDollars"/> exposes the decimal amount for display.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }

    /// <summary>Stable API handle (e.g. <c>eshop-pro</c>). Handles are stable across re-seeds; numeric ids are not.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price, in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price, in the currency's major unit (e.g. dollars).</summary>
    public decimal PriceInDollars => PriceInCents / 100m;

    /// <summary>The numerical billing interval (e.g. <c>1</c>).</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit, either <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; init; } = string.Empty;
}
