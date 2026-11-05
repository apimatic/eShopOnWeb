using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Links an eShopOnWeb user and a plan to the Maxio subscription. The row is written (without an id) before Maxio
/// is called, so its composite primary key (user name + plan handle) is the claim that stops a second subscription.
/// </summary>
public class MaxioSubscriptionLink
{
    public string UserName { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public int? MaxioSubscriptionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
