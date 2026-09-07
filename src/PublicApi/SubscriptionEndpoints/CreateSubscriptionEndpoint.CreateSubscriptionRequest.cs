using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseMessage
{
    public string? PlanHandle { get; set; }
    public string? UserId { get; set; }
}
