namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The plan to enrol in, identified by its durable handle.</summary>
    public string PlanHandle { get; set; }

    /// <summary>Administrators only: act on behalf of another user. Ignored when it matches the caller.</summary>
    public string UserReference { get; set; }
}
