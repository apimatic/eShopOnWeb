using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class SubscriptionPlan : BaseEntity, IAggregateRoot
{
    public required string Handle { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required long PriceInCents { get; set; }
    public required int Interval { get; set; }
    public required string IntervalUnit { get; set; }
    public long MaxioProductId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
