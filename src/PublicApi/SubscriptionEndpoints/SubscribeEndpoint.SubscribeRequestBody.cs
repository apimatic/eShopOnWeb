namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The client-supplied part of a subscribe request. The shopper's identity is never taken from
/// the request body - it comes from the caller's JWT.
/// </summary>
public class SubscribeRequestBody
{
    public string PlanHandle { get; set; } = string.Empty;
}
