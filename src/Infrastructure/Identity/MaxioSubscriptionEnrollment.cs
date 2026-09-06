using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable ownership and idempotency record for a subscription created by eShopOnWeb.
/// Maxio remains the source of truth for billing state and dates.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long? MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = Pending;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }

    public const string Pending = "pending";
    public const string Completed = "completed";
}
