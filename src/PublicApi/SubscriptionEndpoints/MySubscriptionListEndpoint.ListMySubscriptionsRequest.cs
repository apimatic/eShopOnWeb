namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for <c>GET api/my-subscriptions</c>.
/// </summary>
/// <remarks>
/// Carries only the user name lifted from the bearer token; the endpoint exposes no parameters, so
/// there is nothing a caller could set to read another shopper's subscriptions.
/// </remarks>
public class ListMySubscriptionsRequest : BaseRequest
{
    public ListMySubscriptionsRequest(string userName)
    {
        UserName = userName;
    }

    public string UserName { get; }
}
