using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps (user, plan) to a Maxio subscription. The composite primary key is the duplicate-prevention
/// claim: the row is inserted BEFORE the Maxio call is made.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    /// <summary>Null while the claim is in flight / outcome unknown.</summary>
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
}
