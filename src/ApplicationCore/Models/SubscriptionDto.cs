using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A shopper's subscription as recorded in the billing system.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
