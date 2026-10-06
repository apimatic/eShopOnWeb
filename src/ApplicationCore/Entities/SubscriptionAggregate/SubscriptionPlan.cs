namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring subscription plan (a Maxio Advanced Billing product) that a shopper can subscribe to.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id, string handle, string name, string? description, decimal price, int interval, string intervalUnit)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public decimal Price { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
}
