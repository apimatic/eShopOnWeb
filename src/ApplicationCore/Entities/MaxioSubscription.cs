using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioSubscription : BaseEntity, IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
