namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// An application-level view of a subscribable plan, projected from a Maxio product
/// belonging to the configured product family. Wire/transport details of Maxio are
/// intentionally not exposed here.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        string formattedPrice, int interval, string intervalUnit, string? productFamilyHandle)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        FormattedPrice = formattedPrice;
        Interval = interval;
        IntervalUnit = intervalUnit;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The stable API handle of the plan (e.g. "eshop-pro"). Use this to subscribe.</summary>
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }
    public long PriceInCents { get; }
    /// <summary>Human readable price, e.g. "$299.00".</summary>
    public string FormattedPrice { get; }
    /// <summary>Billing interval count, e.g. 1.</summary>
    public int Interval { get; }
    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; }
    public string? ProductFamilyHandle { get; }
}
