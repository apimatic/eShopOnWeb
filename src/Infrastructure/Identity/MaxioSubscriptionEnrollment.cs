using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable admission ledger for a user's request to subscribe to a Maxio product.
/// The unique user/product key makes client retries and concurrent requests join one intent.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
