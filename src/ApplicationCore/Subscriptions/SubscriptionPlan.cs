namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A billing plan a shopper can subscribe to. This is a projection of a Maxio Advanced Billing
/// "product" that belongs to the configured product family. Handles are stable across re-seeds;
/// numeric ids are not, so <see cref="Handle"/> is the value callers should use to subscribe.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        string? description,
        int priceInCents,
        string currency,
        string intervalUnit,
        int intervalCount,
        bool requiresPaymentMethod,
        int productId)
    {
        Handle = handle;
        Name = name;
        Description = description;
        PriceInCents = priceInCents;
        Currency = currency;
        IntervalUnit = intervalUnit;
        IntervalCount = intervalCount;
        RequiresPaymentMethod = requiresPaymentMethod;
        ProductId = productId;
    }

    /// <summary>Stable API handle of the plan (e.g. "eshop-pro"). Use this to subscribe.</summary>
    public string Handle { get; }
    public string Name { get; }
    public string? Description { get; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; }

    /// <summary>Recurring price expressed in major currency units.</summary>
    public decimal Price => PriceInCents / 100m;

    public string Currency { get; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; }

    /// <summary>Number of interval units per billing period, e.g. 1.</summary>
    public int IntervalCount { get; }

    /// <summary>True when the underlying product requires a card on file to subscribe.</summary>
    public bool RequiresPaymentMethod { get; }

    /// <summary>Maxio numeric product id. Unstable across re-seeds; informational only.</summary>
    public int ProductId { get; }
}
