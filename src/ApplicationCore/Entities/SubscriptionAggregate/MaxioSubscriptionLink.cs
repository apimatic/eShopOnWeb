using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public enum MaxioSubscriptionLinkStatus
{
    /// <summary>Claimed; the Maxio call is in flight.</summary>
    Claimed = 0,
    /// <summary>Maxio subscription created and recorded.</summary>
    Created = 1,
    /// <summary>Connection failed after the request may have been processed; settled by re-reading Maxio.</summary>
    Unknown = 2
}

/// <summary>
/// Claim + mapping of (user, plan) to a Maxio subscription. The composite primary key refuses a
/// second concurrent subscribe to the same plan before it reaches Maxio.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    /// <summary>Reference sent to Maxio as the subscription reference; used to reconcile.</summary>
    public string Reference { get; set; } = string.Empty;
    public MaxioSubscriptionLinkStatus Status { get; set; } = MaxioSubscriptionLinkStatus.Claimed;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
