using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string? PlanHandle { get; set; }
}
