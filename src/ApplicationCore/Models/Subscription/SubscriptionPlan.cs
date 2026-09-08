using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A subscribable plan, as offered by Maxio Advanced Billing.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string description, long priceInCents,
        int interval, string intervalUnit, string productFamilyHandle, bool requireCreditCard)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
        RequireCreditCard = requireCreditCard;
    }

    public string Handle { get; }
    public string Name { get; }
    public string Description { get; }
    public long PriceInCents { get; }
    public int Interval { get; }
    public string IntervalUnit { get; }
    public string ProductFamilyHandle { get; }
    public bool RequireCreditCard { get; }

    public string FormattedPrice => $"${(PriceInCents / 100m):0.00}";
}
