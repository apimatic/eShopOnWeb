namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A recurring subscription plan a shopper can subscribe to. This is a domain-level view of a
/// Maxio product within the configured product family; it deliberately carries no billing-SDK types.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>The stable API handle of the plan (e.g. <c>eshop-pro</c>). Handles are stable; ids are not.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Human-friendly plan name.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in integer cents (the billing system's unit of record).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price expressed in major currency units (cents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit wire value (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; set; }
}
