using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string Reference { get; set; }
    public string PlanHandle { get; set; }
    public string PlanName { get; set; }
    public long PriceInCents { get; set; }
    public string Price { get; set; }
    public string State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public bool AlreadySubscribed { get; set; }
}