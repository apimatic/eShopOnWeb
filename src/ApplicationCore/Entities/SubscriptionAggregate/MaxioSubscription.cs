using System;

using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public class MaxioSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = null!;
    public long MaxioCustomerId { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public decimal? CurrentPriceInCents { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? NextBillingAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancellationMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
