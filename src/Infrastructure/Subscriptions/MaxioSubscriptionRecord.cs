using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class MaxioSubscriptionRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
