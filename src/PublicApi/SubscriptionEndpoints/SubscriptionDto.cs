using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The current state of a shopper's subscription, as recorded in Maxio.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public int PlanId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int CustomerId { get; set; }
}
