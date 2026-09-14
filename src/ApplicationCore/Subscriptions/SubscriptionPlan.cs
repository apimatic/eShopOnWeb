namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring-billing plan a shopper can subscribe to. This is a billing-system-agnostic
/// projection of a Maxio product belonging to the configured product family.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        string currency)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Currency = currency;
    }

    /// <summary>The stable API handle of the plan (e.g. <c>eshop-pro</c>). Used to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; }

    /// <summary>The recurring price expressed as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>The numeric billing interval (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing interval unit, either <c>day</c> or <c>month</c>.</summary>
    public string IntervalUnit { get; }

    /// <summary>ISO currency code (e.g. USD).</summary>
    public string Currency { get; }
}
