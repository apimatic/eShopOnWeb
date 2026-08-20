using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public class SubscriptionRequest
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string ProductHandle { get; set; } = null!;
    public string ProviderReference { get; set; } = null!;
    public int? ProviderSubscriptionId { get; set; }
    public SubscriptionRequestStatus Status { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] Version { get; set; } = Array.Empty<byte>();
}

public enum SubscriptionRequestStatus
{
    InProgress,
    Completed,
    OutcomeUnknown
}
