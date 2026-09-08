using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription plan offered by the shop, as read from the Maxio catalog
/// (a Maxio "product" inside the configured product family).
/// </summary>
public class MaxioPlan
{
    public MaxioPlan(int productId,
        string handle,
        string name,
        string? description,
        int priceInCents,
        int interval,
        string intervalUnit,
        bool archived)
    {
        ProductId = productId;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Archived = archived;
    }

    public int ProductId { get; }
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public bool Archived { get; }

    public decimal Price => PriceInCents / 100m;
}
