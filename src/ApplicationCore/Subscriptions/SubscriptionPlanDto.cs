namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan (a Maxio/Chargify product within the configured product family). SDK-free
/// projection returned to API callers.
/// </summary>
public record SubscriptionPlanDto
{
    public int Id { get; init; }

    /// <summary>Stable API handle (e.g. <c>eshop-pro</c>) — the value callers pass to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (Maxio stores money in cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount in the site currency.</summary>
    public decimal Price { get; init; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit (e.g. <c>month</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }
}
