using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class Subscription : BaseEntity, IAggregateRoot
{
    public required string UserId { get; set; }
    public required int MaxioCustomerId { get; set; }
    public required int MaxioSubscriptionId { get; set; }
    public required string ProductHandle { get; set; }
    public required string ProductName { get; set; }
    public required decimal ProductPriceInCents { get; set; }
    public required string State { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
