namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a shopper can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    /// <summary>Stable identifier of the plan; this is what you post back to subscribe.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Number of <see cref="IntervalUnit"/>s in one billing period.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, for example "month".</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>Human readable billing cadence, for example "1 month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>True when subscribing to this plan needs a payment method on file first.</summary>
    public bool RequiresPaymentMethod { get; set; }

    /// <summary>Product family the plan belongs to.</summary>
    public string? ProductFamilyHandle { get; set; }
}
