using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
}

public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingDate { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;
    public bool Created { get; set; }
}
