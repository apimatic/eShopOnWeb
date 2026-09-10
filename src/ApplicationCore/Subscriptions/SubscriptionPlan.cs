namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can enroll in. Maps to a Maxio "product" living inside the
/// configured product family. The <see cref="Handle"/> is the stable, human-readable identifier
/// used when subscribing (numeric ids are reassigned by Maxio on re-seed and must not be relied on).
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        int id,
        string handle,
        string name,
        string? description,
        long priceInCents,
        int interval,
        string intervalUnit,
        bool requiresPaymentMethod,
        string productFamilyHandle)
    {
        Id = id;
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductFamilyHandle = productFamilyHandle;
    }

    /// <summary>The Maxio product id. Not stable across re-seeds; prefer <see cref="Handle"/>.</summary>
    public int Id { get; }

    /// <summary>The stable API handle of the plan (e.g. "eshop-pro"). Used to subscribe.</summary>
    public string Handle { get; }

    public string Name { get; }

    public string? Description { get; }

    /// <summary>The recurring price in integer cents (Maxio's native unit).</summary>
    public long PriceInCents { get; }

    /// <summary>The numeric billing interval, e.g. 1.</summary>
    public int Interval { get; }

    /// <summary>The interval unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; }

    public string ProductFamilyHandle { get; }
}
