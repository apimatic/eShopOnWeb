using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring billing plan a shopper can subscribe to. Projected from a Maxio
/// product that lives inside the configured product family. Handles are stable across
/// re-seeds; numeric ids are not, so <see cref="Handle"/> is the identifier callers use.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(
        string handle,
        string name,
        int productId,
        long priceInCents,
        string currency,
        int interval,
        string intervalUnit,
        string? description)
    {
        Handle = handle;
        Name = name;
        ProductId = productId;
        PriceInCents = priceInCents;
        Currency = currency;
        Interval = interval;
        IntervalUnit = intervalUnit;
        Description = description;
    }

    /// <summary>Stable, human-readable product handle (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; }

    public string Name { get; }

    /// <summary>Maxio numeric product id. Not stable across re-seeds; informational only.</summary>
    public int ProductId { get; }

    /// <summary>Recurring price in the currency's minor unit (cents).</summary>
    public long PriceInCents { get; }

    public string Currency { get; }

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing interval unit (e.g. <c>month</c>).</summary>
    public string IntervalUnit { get; }

    public string? Description { get; }

    /// <summary>Price expressed as a decimal amount (cents / 100).</summary>
    public decimal Price => decimal.Divide(PriceInCents, 100m);
}
