namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio Product that belongs to the
/// configured product family. Prices are recurring amounts expressed in the smallest currency unit
/// (cents) as returned by Maxio.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        int productId,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public int ProductId { get; }

    /// <summary>Stable Maxio product handle (e.g. <c>eshop-pro</c>). This is what callers subscribe with.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    public long PriceInCents { get; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The numeric portion of the billing period (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing period unit as returned by Maxio: <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; }
}
