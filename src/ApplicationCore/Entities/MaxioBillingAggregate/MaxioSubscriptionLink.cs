using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioBillingAggregate;

/// <summary>
/// Maps (user, plan) to a Maxio subscription. The composite primary key is the duplicate-prevention
/// claim: the row is inserted as Claimed BEFORE the provider is called.
/// </summary>
public class MaxioSubscriptionLink : IAggregateRoot
{
    public string UserId { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public MaxioLinkStatus Status { get; set; }
    public DateTime CreatedUtc { get; set; }
}
