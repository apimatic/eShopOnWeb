namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public int Id { get; set; }

    /// <summary>The durable identifier. Prefer this over <see cref="Id"/>, which the provider reassigns.</summary>
    public string Handle { get; set; }

    public string Name { get; set; }

    public string Description { get; set; }

    /// <summary>The recurring price in whole currency units.</summary>
    public decimal Price { get; set; }

    public int Interval { get; set; }

    public string IntervalUnit { get; set; }

    public bool RequiresPaymentMethod { get; set; }
}
