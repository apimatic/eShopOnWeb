namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    /// <summary>The stable identifier to post back to api/subscriptions in order to subscribe.</summary>
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    /// <summary>The recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }
    /// <summary>The recurring price in minor units, exactly as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }
    /// <summary>How many IntervalUnits make up one billing period.</summary>
    public int Interval { get; set; }
    /// <summary>The billing interval unit, e.g. month.</summary>
    public string IntervalUnit { get; set; }
    public string ProductFamilyHandle { get; set; }
}
