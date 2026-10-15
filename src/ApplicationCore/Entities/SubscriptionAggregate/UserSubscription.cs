using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class UserSubscriptionStatus
{
    public const string Pending = "pending";
    public const string Active = "active";
}

/// <summary>
/// Claim/record of a user's subscription to a plan. The composite key (BuyerId, PlanHandle) is what
/// rejects a second concurrent subscribe. <see cref="Reference"/> is sent to Maxio as the subscription reference.
/// </summary>
public class UserSubscription
{
    public string BuyerId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = UserSubscriptionStatus.Pending;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
