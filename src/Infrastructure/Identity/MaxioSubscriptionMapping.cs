using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// The local idempotency record tying an eShopOnWeb identity to its Maxio enrollment.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string UserReference { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
