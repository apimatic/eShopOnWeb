using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable application-side idempotency record for a Maxio enrollment.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public const string Creating = "Creating";
    public const string Completed = "Completed";
    public const string Failed = "Failed";

    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string PlanHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Guid ConcurrencyToken { get; set; }
}
