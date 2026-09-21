namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionBilling;

/// <summary>
/// A recurring subscription plan a shopper can subscribe to. Maps a Maxio Advanced Billing
/// product (within the configured product family) onto a transport-neutral shape so no SDK
/// type leaks past the Infrastructure boundary.
/// </summary>
public class SubscriptionPlanInfo
{
    /// <summary>Stable API handle of the plan (e.g. <c>eshop-pro</c>). Use this to subscribe.</summary>
    public string Handle { get; init; } = string.Empty;

    /// <summary>Human-friendly plan name.</summary>
    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Recurring price in integer cents (Maxio's native unit).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (<see cref="PriceInCents"/> / 100).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>Billing interval count (e.g. 1).</summary>
    public int? Interval { get; init; }

    /// <summary>Billing interval unit wire value (e.g. <c>month</c>, <c>day</c>).</summary>
    public string? IntervalUnit { get; init; }

    /// <summary>Maxio numeric product id. Not stable across re-seeds — prefer <see cref="Handle"/>.</summary>
    public int? ProductId { get; init; }
}
