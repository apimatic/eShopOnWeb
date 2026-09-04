using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable cross-reference between an eShop user and the corresponding Maxio records.
/// Maxio remains the system of record for the subscription state and billing dates.
/// </summary>
public class SubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
