namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio product within the configured product family).
/// Prices are recurring per <see cref="Interval"/> <see cref="IntervalUnit"/>.
/// </summary>
public sealed class SubscriptionPlan
{
    public int Id { get; init; }
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public int PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    public string Currency { get; init; } = "USD";

    /// <summary>Number of <see cref="IntervalUnit"/>s between charges (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit, e.g. "month".</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>True when the plan can be subscribed to without capturing a payment method.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
