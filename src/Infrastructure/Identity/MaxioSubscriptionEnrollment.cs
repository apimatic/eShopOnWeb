using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Idempotency/audit linkage for subscriptions. Subscription state is always read from Maxio.</summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
