namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>An available subscription plan, surfaced from the Maxio product family.</summary>
public class SubscriptionPlan
{
    /// <summary>Stable Maxio plan handle (e.g. <c>eshop-pro</c>). Used to subscribe.</summary>
    public string? Handle { get; set; }

    public string? Name { get; set; }

    /// <summary>Recurring price in dollars (price in cents divided by 100).</summary>
    public decimal? PriceAmount { get; set; }

    /// <summary>Billing frequency magnitude (e.g. 1 for every interval).</summary>
    public int? Interval { get; set; }

    /// <summary>Billing frequency unit: <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }
}
