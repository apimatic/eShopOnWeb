namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }

    /// <summary>Recurring price in major currency units (e.g. 299.00 for $299.00).</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
    public bool RequiresPaymentMethod { get; set; }
    public bool IsArchived { get; set; }
}
