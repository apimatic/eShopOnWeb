using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Maps (user, plan) to a billing-system subscription. The composite primary key is the claim.
/// </summary>
public class MaxioSubscriptionLink : IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string SubscriptionReference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public LinkStatus Status { get; set; }
    public DateTime ClaimedAtUtc { get; set; }
}
