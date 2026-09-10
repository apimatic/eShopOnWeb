namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan surfaced to shoppers. Maps to a Maxio "product" belonging to the
/// configured product family.
/// </summary>
public sealed record SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, string? description, long priceInCents,
        int interval, string intervalUnit, string currencyCode)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        CurrencyCode = currencyCode;
    }

    /// <summary>Stable API handle of the plan; used when subscribing.</summary>
    public string Handle { get; }

    /// <summary>Human-readable plan name.</summary>
    public string Name { get; }

    /// <summary>Optional plan description.</summary>
    public string? Description { get; }

    /// <summary>Recurring price of the plan in integer cents.</summary>
    public long PriceInCents { get; }

    /// <summary>The numeric billing interval (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; }

    /// <summary>ISO currency code the price is expressed in (e.g. "USD").</summary>
    public string CurrencyCode { get; }
}
