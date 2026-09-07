using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public long MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal CurrentPrice { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public string BillingPeriodUnit { get; set; } = null!;
    public int BillingPeriodLength { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
