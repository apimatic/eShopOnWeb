using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable local claim that prevents concurrent enrollment in the same plan.</summary>
public class SubscriptionEnrollment
{
    public required string UserId { get; init; }
    public required string ProductHandle { get; init; }
    public required string RequestReference { get; init; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
