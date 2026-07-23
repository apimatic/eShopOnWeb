namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    /// <summary>The billing provider's numeric id. Reassigned whenever the catalog is re-seeded.</summary>
    public int Id { get; set; }

    /// <summary>The durable identifier used to subscribe to, or change to, this plan.</summary>
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>The recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }

    /// <summary>The recurring price in minor units, as the billing provider reports it.</summary>
    public int PriceInCents { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>A display form of the billing period, e.g. "month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;
}
