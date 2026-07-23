namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to. The handle is the durable identifier;
/// the provider assigns (and reassigns) the numeric id, so never persist it as a key.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id,
        string handle,
        string name,
        string? description,
        decimal price,
        int interval,
        string intervalUnit)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public int Id { get; private set; }

    /// <summary>The stable API handle, e.g. "eshop-pro".</summary>
    public string Handle { get; private set; }

    public string Name { get; private set; }
    public string? Description { get; private set; }

    /// <summary>The recurring price in whole currency units (e.g. 299.00), not minor units.</summary>
    public decimal Price { get; private set; }

    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; }
}
