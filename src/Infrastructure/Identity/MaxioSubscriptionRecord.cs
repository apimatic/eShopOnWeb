using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable idempotency record for an enrollment owned by an eShopOnWeb identity user.
/// </summary>
public class MaxioSubscriptionRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
