namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscribable plan. Sourced from a Maxio (Advanced Billing) product that lives inside the
/// configured product family. Handles are stable across re-seeds; numeric ids are not, so the
/// handle is the identifier used when subscribing.
/// </summary>
public class SubscriptionPlan
{
    public int Id { get; init; }

    /// <summary>The stable API handle of the plan (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>Recurring price, in integer cents, as returned by Maxio.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Convenience view of <see cref="PriceInCents"/> as a decimal amount.</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit as returned by Maxio (<c>month</c> or <c>day</c>).</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>Whether Maxio requires a payment method to subscribe to this plan.</summary>
    public bool RequireCreditCard { get; init; }
}
