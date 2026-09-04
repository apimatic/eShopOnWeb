using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public string? State { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
