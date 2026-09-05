namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>The buyer identity (from the bearer token) combined with the requested plan handle.</summary>
public class SubscribeRequest : BaseRequest
{
    public string BuyerEmail { get; }
    public string PlanHandle { get; }

    public SubscribeRequest(string buyerEmail, string planHandle)
    {
        BuyerEmail = buyerEmail;
        PlanHandle = planHandle;
    }
}
