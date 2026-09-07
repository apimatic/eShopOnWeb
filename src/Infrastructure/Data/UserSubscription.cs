using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.Infrastructure.Data;

public class UserSubscription : BaseEntity
{
    public string UserId { get; set; } = null!;
    public int MaxioSubscriptionId { get; set; }
    public int? MaxioProductId { get; set; }
    public string ProductHandle { get; set; } = null!;
    public string State { get; set; } = null!;
    public long? BalanceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
