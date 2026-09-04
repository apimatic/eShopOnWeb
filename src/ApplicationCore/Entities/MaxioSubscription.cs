using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// The durable link between an eShopOnWeb identity and its Maxio records.
/// Maxio remains the billing system of record; this entity is only the local
/// correlation and idempotency record.
/// </summary>
public class MaxioSubscription : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int MaxioSubscriptionId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
