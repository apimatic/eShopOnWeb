using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable record of an application's enrollment request and its Maxio identifiers.
/// The unique user/plan key is the local idempotency boundary.
/// </summary>
public class SubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public double? MaxioCustomerId { get; set; }
    public double? MaxioSubscriptionId { get; set; }
    public required string Status { get; set; }
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public string? Currency { get; set; }
    public string? SubscriptionState { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
