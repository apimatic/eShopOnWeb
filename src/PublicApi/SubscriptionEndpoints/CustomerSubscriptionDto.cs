using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; }
    public string PaymentCollectionMethod { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
