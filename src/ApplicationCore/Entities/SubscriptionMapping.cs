using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable correlation between an eShopOnWeb user and a Maxio subscription.
/// Maxio remains the source of truth for the subscription state and billing dates.
/// </summary>
public class SubscriptionMapping : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
