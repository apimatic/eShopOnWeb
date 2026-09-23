namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscribable plan, projected from a Maxio product in the configured product family.
/// </summary>
public record SubscriptionPlanInfo
{
    public required string Handle { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (Maxio <c>price_in_cents</c>).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price formatted for display, e.g. <c>$299.00</c>.</summary>
    public required string FormattedPrice { get; init; }

    /// <summary>Billing interval, e.g. <c>1</c>.</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit wire value, e.g. <c>month</c>.</summary>
    public string? IntervalUnit { get; init; }

    public int? ProductId { get; init; }
}
