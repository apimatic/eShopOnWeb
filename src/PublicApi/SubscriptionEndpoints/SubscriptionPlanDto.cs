namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscribable plan (Maxio product) as surfaced to API consumers.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>The Maxio product id.</summary>
    public int Id { get; set; }

    /// <summary>The stable Maxio product handle (e.g. "eshop-pro").</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price of the plan's default price point, in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Billing interval length (e.g. 1 for monthly).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing interval unit: "month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    /// <summary>True when Maxio requires a stored payment method before this plan can be subscribed to.</summary>
    public bool RequiresPaymentMethod { get; set; }

    public string? PricePointName { get; set; }

    public string? PricePointHandle { get; set; }
}
