using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

public sealed class SubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required string CustomerReference { get; set; }
    public required string SubscriptionReference { get; set; }
    public int? MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public required string Status { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
    public string? LastFailureCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}
