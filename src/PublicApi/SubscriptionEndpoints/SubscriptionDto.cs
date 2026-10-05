using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The state of a shopper's subscription in Maxio Advanced Billing.
/// </summary>
public class SubscriptionDto
{
    public int MaxioSubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public string? Currency { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
}