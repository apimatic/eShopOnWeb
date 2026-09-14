namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio product within the configured product family),
/// expressed in application terms rather than raw Maxio JSON.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>Stable API handle of the plan (used as the subscribe target).</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Human-friendly plan name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional plan description.</summary>
    public string? Description { get; init; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>ISO currency code (e.g. "USD").</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>The numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>The billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Handle of the default price point for this plan, when available.</summary>
    public string? PricePointHandle { get; init; }

    /// <summary>Whether a stored payment method is required to subscribe.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
