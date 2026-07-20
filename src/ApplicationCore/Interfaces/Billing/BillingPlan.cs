namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

public class BillingPlan
{
    public BillingPlan(int id, string handle, string? name, long priceInCents, string? intervalUnit, int? interval)
    {
        Id = id;
        Handle = handle;
        Name = name;
        PriceInCents = priceInCents;
        IntervalUnit = intervalUnit;
        Interval = interval;
    }

    public int Id { get; }
    public string Handle { get; }
    public string? Name { get; }
    public long PriceInCents { get; }
    public decimal Price => PriceInCents / 100m;
    public string? IntervalUnit { get; }
    public int? Interval { get; }
}
