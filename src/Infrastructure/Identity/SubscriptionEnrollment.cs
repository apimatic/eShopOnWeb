using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable application-side reservation for one user's enrollment in one Maxio plan.
/// Maxio remains the billing system of record; this record prevents duplicate submits
/// and lets a retry recover a request that completed remotely before it completed locally.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
