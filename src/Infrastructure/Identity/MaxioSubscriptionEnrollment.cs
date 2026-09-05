using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A local idempotency claim. Maxio remains the source of truth for customer and subscription state.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public long? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
