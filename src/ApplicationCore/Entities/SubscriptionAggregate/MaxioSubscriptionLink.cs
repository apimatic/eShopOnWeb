using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Claim + record of a Maxio subscription for (user, plan). The composite primary key is the
/// duplicate guard: a second subscribe to the same plan by the same user is refused by the store.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    /// <summary>Sent to Maxio as the subscription reference; used to reconcile unknown outcomes.</summary>
    public string Reference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public SubscriptionLinkState State { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum SubscriptionLinkState
{
    /// <summary>Claim taken; the Maxio call is in flight.</summary>
    Pending = 0,
    Active = 1,
    /// <summary>The Maxio call failed in a way that leaves its outcome unknown; to be settled by reference.</summary>
    Unknown = 2
}
