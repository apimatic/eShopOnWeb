namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, projected from a Maxio product within the configured product family.
/// </summary>
public record SubscriptionPlan
{
    /// <summary>The stable Maxio product handle (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>The Maxio numeric product id (not stable across catalog re-seeds).</summary>
    public int ProductId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Numeric billing interval (e.g. <c>1</c>).</summary>
    public int Interval { get; init; }

    /// <summary>Interval unit as reported by Maxio (<c>day</c> or <c>month</c>).</summary>
    public string IntervalUnit { get; init; } = "month";

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequireCreditCard { get; init; }

    /// <summary>Convenience projection of <see cref="PriceInCents"/> to whole currency units.</summary>
    public decimal Price => PriceInCents / 100m;
}
