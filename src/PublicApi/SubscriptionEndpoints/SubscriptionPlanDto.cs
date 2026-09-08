namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>Describes a plan a shopper can subscribe to.</summary>
public class SubscriptionPlanDto
{
    /// <summary>The Maxio product (plan) id.</summary>
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Handle { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Plan price in the site currency (e.g. 299.00).</summary>
    public decimal Price { get; set; }

    /// <summary>Billing interval length (e.g. 1).</summary>
    public int Interval { get; set; }

    /// <summary>Billing interval unit (day, month, year...).</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Whether subscribing to this plan requires a stored payment method.</summary>
    public bool RequiresCreditCard { get; set; }

    public bool? Taxable { get; set; }
}
