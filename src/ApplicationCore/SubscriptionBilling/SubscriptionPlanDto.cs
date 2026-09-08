namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscribable plan (a Maxio "product") surfaced to shoppers.
/// </summary>
public class SubscriptionPlanDto
{
    public string ProductHandle { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    /// <summary>The recurring price in minor currency units (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>The recurring price expressed as a decimal amount of the site currency.</summary>
    public decimal Price { get; init; }

    /// <summary>Billing interval length (e.g. 1 for monthly).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit: "month" or "day".</summary>
    public string IntervalUnit { get; init; } = "month";

    /// <summary>Whether capturing a payment method is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; init; }

    public string? ProductPricePointName { get; init; }

    public string? ProductPricePointHandle { get; init; }

    public bool Archived { get; init; }
}
