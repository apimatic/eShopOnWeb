using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Application-owned enrollment intent. The unique user/plan key is the durable
/// idempotency boundary for the provider write.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string PlanHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public required string Status { get; set; }
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string? PlanName { get; set; }
    public int? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? SubscriptionState { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public string? LeaseToken { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
