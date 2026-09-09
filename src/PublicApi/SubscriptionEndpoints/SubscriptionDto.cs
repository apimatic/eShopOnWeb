using System;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The current billing-system view of the caller's subscription.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime? NextBillingDate { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public int MaxioCustomerId { get; set; }
    public string? MaxioReference { get; set; }
}
