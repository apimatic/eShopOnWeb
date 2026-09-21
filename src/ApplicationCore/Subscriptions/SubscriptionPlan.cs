namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, projected from a Maxio product in the configured product family.
/// This is a plain domain DTO so callers (e.g. PublicApi) never depend on the Maxio SDK types.
/// </summary>
public class SubscriptionPlan
{
    /// <summary>The stable product API handle (e.g. <c>eshop-pro</c>); the value callers pass to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Human-readable plan name (e.g. <c>Pro Plan</c>).</summary>
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (e.g. 29900 for $299.00).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Currency-formatted price for display (e.g. <c>$299.00</c>).</summary>
    public string PriceFormatted { get; init; } = string.Empty;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit (e.g. <c>month</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
