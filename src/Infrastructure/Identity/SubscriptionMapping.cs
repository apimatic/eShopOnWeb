using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable correlation between an eShop user and the corresponding Maxio records.
/// Maxio remains the billing system of record; this entity only prevents the app from
/// losing the identifiers needed to retrieve those records.
/// </summary>
public class SubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
