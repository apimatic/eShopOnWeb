namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API view of a subscribable plan.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle used to subscribe (e.g. <c>eshop-pro</c>).</summary>
    public string Handle { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price in major units.</summary>
    public decimal Price { get; set; }

    /// <summary>Billing period length (paired with <see cref="IntervalUnit"/>).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing period unit (e.g. <c>month</c>).</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>ISO currency code, when known. Null for plans (Maxio does not expose currency on products).</summary>
    public string? Currency { get; set; }
}
