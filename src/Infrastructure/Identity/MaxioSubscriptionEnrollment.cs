using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A single enrollment operation for a user and product handle. The unique index is the
/// durable backstop for duplicate submits across application instances.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string ProductHandle { get; set; }
    public required int MaxioCustomerId { get; set; }
    public required string SubscriptionReference { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
