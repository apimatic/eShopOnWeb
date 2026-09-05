using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable idempotency and read model for a Maxio subscription enrollment.</summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? SubscriptionId { get; set; }
    public string? State { get; set; }
    public string? ProductName { get; set; }
    public long? PriceInCents { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ProcessingLeaseExpiresUtc { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
