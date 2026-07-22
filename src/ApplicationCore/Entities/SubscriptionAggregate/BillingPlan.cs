namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// A recurring plan a customer can subscribe to, normalized from the billing provider.
/// </summary>
public class BillingPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>The recurring price in major currency units (e.g. 299.00 — not cents).</summary>
    public decimal Price { get; init; }

    /// <summary>The numeric part of the billing interval, e.g. 1 in "every 1 month".</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit as reported by the provider, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;
    public string? ProductFamilyHandle { get; init; }
    public bool RequiresPaymentMethod { get; init; }
    public bool Archived { get; init; }
}
