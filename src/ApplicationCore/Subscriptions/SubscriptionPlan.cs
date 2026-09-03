namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan, projected from a Maxio product in the configured product family. Handles are the
/// stable identifier (numeric ids are reassigned on re-seed), so <see cref="Handle"/> is what callers pass
/// back to subscribe.
/// </summary>
public sealed class SubscriptionPlan
{
    public int ProductId { get; init; }

    /// <summary>Stable product API handle, e.g. <c>eshop-pro</c>. This is the value passed to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (Maxio stores money in cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Convenience decimal rendering of <see cref="PriceInCents"/> (cents / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count, e.g. <c>1</c>.</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit as reported by Maxio, e.g. <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan (the seeded plans do not).</summary>
    public bool RequiresPaymentMethod { get; init; }
}
