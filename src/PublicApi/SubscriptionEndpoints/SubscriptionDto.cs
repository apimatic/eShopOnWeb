using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as exposed over the API. Monetary values are in whole currency units.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string Status { get; set; }
    public string ProviderState { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal PlanPrice { get; set; }
    public decimal Balance { get; set; }
    public string CustomerReference { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public string NextPlanHandle { get; set; }
}
