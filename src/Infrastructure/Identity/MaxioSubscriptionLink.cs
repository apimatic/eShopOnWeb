using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable correlation between an eShopOnWeb user and a subscription in Maxio.
/// Maxio remains the billing system of record; this entity is an integration index.
/// </summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public long MaxioCustomerId { get; set; }
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}
