using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable idempotency record for a subscription enrollment request.
/// Maxio remains the system of record for customer and subscription state.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = StatusPending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public const string StatusPending = "pending";
    public const string StatusCompleted = "completed";
    public const string StatusFailed = "failed";
}
