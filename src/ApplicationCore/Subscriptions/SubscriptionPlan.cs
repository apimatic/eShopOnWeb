namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription plan a shopper can subscribe to. This is a provider-agnostic view of a
/// billing "product" — prices are always expressed in integer minor units (cents) so no
/// rounding is introduced in the domain layer.
/// </summary>
public class SubscriptionPlan
{
    public SubscriptionPlan(string handle, string name, int productId, long priceInCents, int interval, string intervalUnit)
    {
        Handle = handle;
        Name = name;
        ProductId = productId;
        PriceInCents = priceInCents;
        Interval = interval;
        IntervalUnit = intervalUnit;
    }

    /// <summary>Stable plan identifier (e.g. "eshop-pro"). Handles survive re-seeds; numeric ids do not.</summary>
    public string Handle { get; }

    /// <summary>Human-friendly plan name.</summary>
    public string Name { get; }

    /// <summary>Numeric product id in the billing system (not stable across re-seeds).</summary>
    public int ProductId { get; }

    /// <summary>Recurring price in integer minor units (cents).</summary>
    public long PriceInCents { get; }

    /// <summary>Number of interval units between charges (e.g. 1).</summary>
    public int Interval { get; }

    /// <summary>Billing interval unit wire value (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; }
}
