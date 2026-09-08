namespace Microsoft.eShopWeb.ApplicationCore.Subscription;

/// <summary>
/// A recurring subscription plan available for shoppers to subscribe to.
/// Sourced from the billing system of record (Maxio Advanced Billing products).
/// </summary>
public sealed class SubscriptionPlan
{
    /// <summary>The billing system's product id.</summary>
    public int Id { get; init; }

    /// <summary>The billing system's stable API handle for the product.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in major currency units (e.g. dollars).</summary>
    public decimal Price { get; init; }

    /// <summary>Recurring price in minor currency units (e.g. cents), as reported by the billing system.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Billing interval count (e.g. 1 = every interval-unit).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit, e.g. "month" or "day".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>True when the plan demands a payment method at signup.</summary>
    public bool RequiresPaymentMethod { get; init; }

    /// <summary>Handle of the product family this plan belongs to.</summary>
    public string ProductFamilyHandle { get; init; } = string.Empty;
}
