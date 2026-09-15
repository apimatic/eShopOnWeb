using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>Durable application-side idempotency record for a user's plan enrollment.</summary>
public class SubscriptionEnrollment : BaseEntity
{
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public string? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
