using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable idempotency record for a subscription request.
/// A null MaxioSubscriptionId means that creation is currently being completed.
/// </summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public int? MaxioSubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
