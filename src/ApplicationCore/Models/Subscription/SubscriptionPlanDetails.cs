using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A subscription plan available for purchase, as advertised by the billing system.
/// </summary>
public class SubscriptionPlanDetails
{
    public SubscriptionPlanDetails(string handle, string name, string productFamilyHandle,
        int priceInCents, int interval, string intervalUnit, bool taxable)
    {
        Handle = handle;
        Name = name;
        ProductFamilyHandle = productFamilyHandle;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Taxable = taxable;
    }

    public string Handle { get; }
    public string Name { get; }
    public string ProductFamilyHandle { get; }
    public int PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public bool Taxable { get; }

    public decimal Price => PriceInCents / 100m;
}
