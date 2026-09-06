using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public long MaxioCustomerId { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string PlanName { get; set; } = null!;
    public decimal PriceInDollars { get; set; }
    public string BillingState { get; set; } = null!;
    public DateTime? CurrentPeriodStartsAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
