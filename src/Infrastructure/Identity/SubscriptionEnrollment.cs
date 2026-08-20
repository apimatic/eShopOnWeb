using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public enum SubscriptionEnrollmentState
{
    Pending,
    Created,
    Failed,
    Indeterminate
}

public sealed class SubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required string MaxioSubscriptionReference { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public SubscriptionEnrollmentState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ApplicationUser? User { get; set; }
}
