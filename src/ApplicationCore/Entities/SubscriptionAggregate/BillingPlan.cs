namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, as presented by the billing provider.
/// Prices are expressed in dollars — the seam converts from whatever unit the provider uses.
/// </summary>
public class BillingPlan
{
    public BillingPlan(int id, string handle, string name, string? description, decimal price,
        int interval, string intervalUnit, bool requiresPaymentMethod, bool archived)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        Price = price;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        Archived = archived;
    }

    /// <summary>Provider-assigned identifier. Not stable across a sandbox re-seed — resolve by <see cref="Handle"/>.</summary>
    public int Id { get; }

    /// <summary>The durable identifier the integration is configured with.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>Recurring price in dollars.</summary>
    public decimal Price { get; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>The billing period unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>True when the provider demands a payment method before enrollment can succeed.</summary>
    public bool RequiresPaymentMethod { get; }

    public bool Archived { get; }

    /// <summary>A display string such as "$299.00 / month".</summary>
    public string PriceDisplay =>
        Interval == 1
            ? $"{BillingMoney.ToDisplay(Price)} / {IntervalUnit}"
            : $"{BillingMoney.ToDisplay(Price)} / {Interval} {IntervalUnit}s";
}
