namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Wire shape of the POST body. The subscriber's identity is never taken from the request body -
/// it always comes from the caller's JWT, so it has no place here.
/// </summary>
public class SubscribeRequestBody
{
    public string PlanHandle { get; set; } = string.Empty;
}
