using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable application-side reservation for an idempotent Maxio enrollment.
/// Maxio remains the source of truth for subscription state and billing details.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public string Status { get; set; } = SubscriptionEnrollmentStatus.Processing;
    public int? MaxioSubscriptionId { get; set; }
    public string? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public static class SubscriptionEnrollmentStatus
{
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Retryable = "retryable";
}
