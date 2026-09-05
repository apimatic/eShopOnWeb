using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Local idempotency record. Billing state remains authoritative in Maxio.</summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
