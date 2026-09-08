namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for recurring purchase.
/// </summary>
public class SubscriptionPlanDto
{
    public SubscriptionPlanDto(string handle, string name, string productFamilyHandle,
        int priceInCents, decimal price, int interval, string intervalUnit, bool taxable)
    {
        Handle = handle;
        Name = name;
        ProductFamilyHandle = productFamilyHandle;
        PriceInCents = priceInCents;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Taxable = taxable;
    }

    public string Handle { get; }
    public string Name { get; }
    public string ProductFamilyHandle { get; }
    public int PriceInCents { get; }
    public decimal Price { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public bool Taxable { get; }
}
