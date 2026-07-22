namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>A recurring plan a customer can subscribe to.</summary>
public class SubscriptionPlanDto
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The recurring price in major currency units, for example <c>299.00</c>.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}
