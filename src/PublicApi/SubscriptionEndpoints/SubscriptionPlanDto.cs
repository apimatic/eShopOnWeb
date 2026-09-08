namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A subscription plan exposed for enrollment (a Maxio product within the configured family).</summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable plan handle (e.g. "eshop-pro"); send this to subscribe.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Recurring price in cents (e.g. 29900 for $299.00).</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount in the site's currency (assumed USD on the seed site).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing frequency magnitude (e.g. 1 for monthly).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing frequency unit: "month" or "day".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when a payment method (card) is required to subscribe to this plan.</summary>
    public bool RequiresPaymentMethod { get; set; }
}
