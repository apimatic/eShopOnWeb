using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int SubscriptionPlanId { get; set; }
    public SubscriptionPlan? SubscriptionPlan { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime? NextBillingDate { get; set; }
    public decimal CurrentPrice { get; set; }
}
