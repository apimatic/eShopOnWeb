using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Local association for a Maxio subscription created by eShopOnWeb.
/// The deterministic Maxio reference is also used for cross-process idempotency.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public long MaxioSubscriptionId { get; set; }
    public long MaxioCustomerId { get; set; }
    public DateTimeOffset LastVerifiedAt { get; set; }
}
