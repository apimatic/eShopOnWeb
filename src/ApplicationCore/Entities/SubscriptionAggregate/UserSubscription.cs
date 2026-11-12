using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps an eShopOnWeb user to a Maxio subscription. The composite primary key (user id, plan handle) is the claim
/// that stops a double-submit from creating two subscriptions to the same plan.
/// </summary>
public class UserSubscription
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The Maxio subscription <c>reference</c> sent on create; the lookup key used to settle unknown outcomes.</summary>
    public string SubscriptionReference { get; set; } = string.Empty;

    /// <summary>Null until the Maxio subscription is known to exist.</summary>
    public int? MaxioSubscriptionId { get; set; }

    public DateTimeOffset ClaimedAt { get; set; } = DateTimeOffset.UtcNow;

    // Last known provider state, served if Maxio is unreachable.
    public string? PlanName { get; set; }
    public long? PriceInCents { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }
}
