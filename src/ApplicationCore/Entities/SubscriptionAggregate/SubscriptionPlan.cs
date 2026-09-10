namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a shopper can subscribe to (a "product" in the billing system), addressed by its
/// stable handle. Prices are held in integer cents, exactly as the billing system reports them.
/// </summary>
public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>The recurring price, in integer cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>The numeric billing interval (e.g. <c>1</c> in "every 1 month").</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit (e.g. <c>month</c> or <c>day</c>).</summary>
    public required string IntervalUnit { get; init; }

    /// <summary>Whether a stored payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public decimal Price => PriceInCents / 100m;
}
