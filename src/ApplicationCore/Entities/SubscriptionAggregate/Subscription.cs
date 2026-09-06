using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public int PlanId { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public decimal? CurrentPrice { get; set; }

    public SubscriptionPlan? Plan { get; set; }
}
