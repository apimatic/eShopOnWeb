using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public long? PriceInCents { get; set; }
    public bool IsDefault { get; set; }
    public bool Available { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}

public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public bool PlanResolved { get; set; }
    public long? PriceInCents { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}