namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. Projected from a Maxio Advanced Billing
/// "product" that belongs to the configured product family. The <see cref="Handle"/> is the
/// stable identifier used when subscribing; numeric ids are intentionally not surfaced because
/// Maxio reassigns them when a site is re-seeded.
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
        bool requiresPaymentMethod)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
    }

    /// <summary>Stable Maxio product handle, e.g. "eshop-pro". Used to subscribe.</summary>
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; }

    /// <summary>Length of a billing cycle (paired with <see cref="IntervalUnit"/>).</summary>
    public int Interval { get; }

    /// <summary>Unit for the billing cycle, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>True when Maxio requires a stored payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; }

    /// <summary>Human-friendly price, e.g. "$299.00/month".</summary>
    public string PriceDisplay =>
        $"${PriceInCents / 100m:0.00}/{(Interval == 1 ? IntervalUnit : $"{Interval} {IntervalUnit}s")}";
}
