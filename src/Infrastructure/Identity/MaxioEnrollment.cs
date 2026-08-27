using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class MaxioEnrollment
{
    public int Id { get; set; }
    public string ApplicationUserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = MaxioEnrollmentStatus.Pending;
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class MaxioEnrollmentStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Failed = "failed";
}
