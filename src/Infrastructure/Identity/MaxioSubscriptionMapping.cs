using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Links an eShop user and plan to the corresponding Maxio records.
/// Maxio remains the billing system of record; this table is the local idempotency index.
/// </summary>
public class MaxioSubscriptionMapping
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string CustomerReference { get; set; } = string.Empty;
    public int MaxioCustomerId { get; set; }
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public string? CreationToken { get; set; }
    public DateTime? CreationStartedAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
