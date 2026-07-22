using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public int BillingCustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    /// <summary>The recurring plan price in the site currency, e.g. 299.00.</summary>
    public decimal PlanPrice { get; set; }

    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
}
