using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring subscription plan (a Maxio "product") offered to shoppers.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(int id, string handle, string name, string? description, int priceInCents, int interval, string intervalUnit, string productFamilyHandle, IReadOnlyList<SubscriptionComponent> components)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        Components = components;
    }

    public int Id { get; }
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string ProductFamilyHandle { get; }
    public IReadOnlyList<SubscriptionComponent> Components { get; }
}
