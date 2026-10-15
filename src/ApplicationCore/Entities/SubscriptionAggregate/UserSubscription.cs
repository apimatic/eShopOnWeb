using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// One subscription per (user, plan). The composite primary key is the claim that keeps a
/// double-click from creating two Maxio subscriptions.
/// </summary>
public class UserSubscription
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public string Status { get; set; } = ClaimStatus.Pending;
    public DateTimeOffset ClaimedAt { get; set; }
    public Guid ClaimToken { get; set; } = Guid.NewGuid();
}
