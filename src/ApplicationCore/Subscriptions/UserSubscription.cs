using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class UserSubscription
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int CustomerId { get; set; }
    public int ProductId { get; set; }
}
