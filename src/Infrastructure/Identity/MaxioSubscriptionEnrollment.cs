using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>Durable idempotency record for a shopper's selected Maxio plan.</summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int MaxioSubscriptionId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
