using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable correlation between an eShop user and the corresponding Maxio subscription.
/// Maxio remains the source of truth for the subscription's live state.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long MaxioCustomerId { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
