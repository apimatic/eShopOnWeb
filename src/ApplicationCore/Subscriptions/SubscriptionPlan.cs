namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, sourced from a Maxio product within the configured product family.
/// </summary>
public sealed class SubscriptionPlan
{
    public int ProductId { get; init; }

    /// <summary>The stable Maxio product API handle (e.g. <c>eshop-pro</c>). This is what callers subscribe to.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (Maxio's native unit).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Number of interval units per billing period (e.g. <c>1</c>).</summary>
    public int Interval { get; init; }

    /// <summary>The interval unit, either <c>month</c> or <c>day</c>.</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Whether Maxio requires a stored payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
