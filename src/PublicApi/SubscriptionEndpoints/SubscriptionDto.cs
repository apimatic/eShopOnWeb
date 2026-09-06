using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    /// <summary>The billing provider's state for this subscription, e.g. active.</summary>
    public string State { get; set; }
    /// <summary>True while the subscription still bills; false once it has reached a terminal state.</summary>
    public bool IsLive { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    /// <summary>The recurring price in major units, e.g. 299.00.</summary>
    public decimal Price { get; set; }
    /// <summary>The recurring price in minor units, exactly as the billing provider reports it.</summary>
    public long PriceInCents { get; set; }
    public string Currency { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
    /// <summary>When the next assessment falls due; null when the provider reports no future billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int CustomerId { get; set; }
    /// <summary>The billing customer reference this API derives from the signed-in account.</summary>
    public string CustomerReference { get; set; }
}
