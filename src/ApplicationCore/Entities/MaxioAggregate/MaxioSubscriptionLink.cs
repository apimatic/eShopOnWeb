using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.MaxioAggregate;

/// <summary>
/// Maps an eShopOnWeb user + plan to its Maxio subscription. The composite primary key
/// (<see cref="UserKey"/>, <see cref="PlanHandle"/>) is the claim that stops a double-submit.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserKey { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public MaxioLinkStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
