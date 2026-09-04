using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable correlation between an eShopOnWeb user and the corresponding Maxio records.
/// Maxio remains the system of record for billing state; this entity is only an integration
/// index and retry/idempotency anchor.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string CustomerReference { get; set; } = string.Empty;

    public long MaxioCustomerId { get; set; }

    public string SubscriptionReference { get; set; } = string.Empty;

    public long MaxioSubscriptionId { get; set; }

    public string ProductHandle { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}
