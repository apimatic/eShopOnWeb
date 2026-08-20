namespace Microsoft.eShopWeb.ApplicationCore.Entities.BillingAggregate;

/// <summary>
/// A sellable Maxio product (plan) in the configured product family.
/// </summary>
public sealed class BillingPlan
{
    public BillingPlan(
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
    public int Interval { get; }
    public string IntervalUnit { get; }
    public decimal Price => PriceInCents / 100m;
}
