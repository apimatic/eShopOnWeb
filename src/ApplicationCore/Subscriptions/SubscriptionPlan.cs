namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring plan a shopper can subscribe to. Projected from a Maxio product within the
/// configured product family. The <see cref="Handle"/> is the stable identifier used when subscribing.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        string currency, int interval, string intervalUnit)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Stable API handle of the plan (e.g. "eshop-pro"). Pass this to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in integer cents (Maxio's canonical representation).</summary>
    public long PriceInCents { get; }

    /// <summary>ISO currency code the price is expressed in (e.g. "USD").</summary>
    public string Currency { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s per billing period (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing period unit, "day" or "month".</summary>
    public string IntervalUnit { get; }
}
