namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, projected from a Maxio product within the configured product family.
/// This is a domain-facing view that deliberately hides the billing SDK's model types.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable plan handle (e.g. <c>eshop-pro</c>). Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Numeric product id in the billing system (not stable across re-seeds).</summary>
    public int? ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price, in the smallest currency unit (cents), charged every <see cref="Interval"/> <see cref="IntervalUnit"/>.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Convenience decimal rendering of <see cref="PriceInCents"/> (major units).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing period length, paired with <see cref="IntervalUnit"/> (e.g. 1 month).</summary>
    public int? Interval { get; init; }

    /// <summary>Billing period unit as reported by the billing system (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>
    /// ISO currency code for <see cref="PriceInCents"/>, when the billing system exposes it.
    /// Maxio does not surface currency on the product/plan model, so this is null for plans
    /// (currency is only known once a subscription exists).
    /// </summary>
    public string? Currency { get; init; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string? ProductFamilyHandle { get; init; }
}
