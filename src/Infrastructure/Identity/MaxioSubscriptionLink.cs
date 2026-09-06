using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local idempotency record for a user's enrollment in a Maxio product.
/// </summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
