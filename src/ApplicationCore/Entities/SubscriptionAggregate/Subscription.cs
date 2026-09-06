using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public int MaxioCustomerId { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Subscription() { }

    public Subscription(string userId, int maxioCustomerId, long maxioSubscriptionId, string planHandle, string status, DateTime? nextBillingDate)
    {
        UserId = userId;
        MaxioCustomerId = maxioCustomerId;
        MaxioSubscriptionId = maxioSubscriptionId;
        PlanHandle = planHandle;
        Status = status;
        NextBillingDate = nextBillingDate;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }
}
