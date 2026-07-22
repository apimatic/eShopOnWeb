namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enrol in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Taken from the bearer token, never from the request body.</summary>
    public string? UserReference { get; set; }
}
