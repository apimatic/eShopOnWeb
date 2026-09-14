namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The JSON body accepted by POST api/subscriptions.</summary>
public class SubscribeRequestBody
{
    public string PlanHandle { get; set; } = string.Empty;
}
