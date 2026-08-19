namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, decimal price, int interval, string intervalUnit)
    {
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public decimal Price { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
}
