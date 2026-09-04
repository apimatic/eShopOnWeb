using System;

namespace Microsoft.eShopWeb.Infrastructure.Identity;

/// <summary>
/// Durable correlation between an eShop user/plan and the subscription owned by Maxio.
/// </summary>
public class MaxioSubscriptionLink
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? CustomerId { get; set; }
    public int? SubscriptionId { get; set; }
    public string Status { get; set; } = PendingStatus;
    public string? ProcessingToken { get; set; }
    public DateTimeOffset? ProcessingUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public const string PendingStatus = "pending";
    public const string ActiveStatus = "created";
}
