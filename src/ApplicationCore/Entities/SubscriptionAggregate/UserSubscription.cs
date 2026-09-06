using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class UserSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public int MaxioSubscriptionId { get; set; }
    public string MaxioCustomerReference { get; set; } = null!;
    public string PlanHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public long PriceInCents { get; set; }
    public string State { get; set; } = null!;
    public DateTimeOffset? NextBillingDate { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
