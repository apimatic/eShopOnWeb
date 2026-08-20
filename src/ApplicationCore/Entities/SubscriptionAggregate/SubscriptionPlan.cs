using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, decimal price, int interval, string intervalUnit)
    {
        Handle = handle;
        Name = name;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public string Handle { get; }
    public string Name { get; }
    public decimal Price { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
}
