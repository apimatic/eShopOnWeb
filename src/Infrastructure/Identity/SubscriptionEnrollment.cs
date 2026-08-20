using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Operational mapping used to make subscription enrollment idempotent. Maxio remains
/// the system of record for billing state and subscription details.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public long? MaxioCustomerId { get; set; }
    public long? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = SubscriptionEnrollmentStatus.Pending;
    public string OperationId { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");

    public ApplicationUser User { get; set; } = null!;
}

public static class SubscriptionEnrollmentStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Failed = "Failed";
}
