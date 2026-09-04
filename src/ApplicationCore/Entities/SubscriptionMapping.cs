using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// Durable correlation between an eShopOnWeb user and the corresponding Maxio subscription.
/// The Maxio reference remains the ultimate idempotency key; this record makes account reads
/// efficient and allows the application to recover after a successful remote call.
/// </summary>
public class SubscriptionMapping : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string State { get; set; } = SubscriptionMappingStates.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public static class SubscriptionMappingStates
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Failed = "failed";
}
