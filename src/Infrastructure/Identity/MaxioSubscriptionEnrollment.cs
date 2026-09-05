using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable idempotency record for a user's enrollment in a Maxio product.
/// Maxio remains the billing system of record; this only correlates identities and retries.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long? MaxioSubscriptionId { get; set; }
    public string UniquenessToken { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
