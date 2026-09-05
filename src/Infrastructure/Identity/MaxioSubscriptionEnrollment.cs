using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// A durable reservation for an application user's enrollment in a Maxio plan.
/// </summary>
public class MaxioSubscriptionEnrollment
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int? MaxioCustomerId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ConfirmedAtUtc { get; set; }
}
