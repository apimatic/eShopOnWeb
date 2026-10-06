using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A user's subscription as reflected in the billing system (Maxio).
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public int BillingInterval { get; set; }
    public string BillingIntervalUnit { get; set; } = string.Empty;
    public DateTime? NextBillingDateUtc { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public long MaxioCustomerId { get; set; }
}