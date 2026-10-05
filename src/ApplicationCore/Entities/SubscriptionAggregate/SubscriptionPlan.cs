namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A subscription plan available for purchase. Sourced from the
/// external billing system of record (Maxio Advanced Billing).
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; private set; }
    public string Name { get; private set; }
    public string Description { get; private set; }
    public long PriceInCents { get; private set; }
    public int Interval { get; private set; }
    public string IntervalUnit { get; private set; }
    public string ProductFamilyHandle { get; private set; }

    public SubscriptionPlan(string handle, string name, string description, long priceInCents,
        int interval, string intervalUnit, string productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }
}