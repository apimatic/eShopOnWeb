namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

public sealed class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        int priceInCents,
        int interval,
        string intervalUnit)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public int PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public int Interval { get; }
    public string IntervalUnit { get; }
}
