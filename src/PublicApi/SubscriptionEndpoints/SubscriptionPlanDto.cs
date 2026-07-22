namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A recurring plan a customer can subscribe to.
/// </summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; }
    public string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Recurring price per billing period, in whole currency units.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
    public string BillingPeriodDescription { get; set; }
    public bool RequiresPaymentMethod { get; set; }
}
