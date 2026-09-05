using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

public class CustomerSubscriptionModel
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
