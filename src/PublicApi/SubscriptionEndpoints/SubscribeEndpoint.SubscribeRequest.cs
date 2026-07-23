namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseMessage
{
    /// <summary>The durable handle of the plan to enroll in, e.g. <c>eshop-pro</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The eShopOnWeb user being enrolled. Populated from the bearer token by the endpoint and never bound
    /// from the request body, so a caller cannot subscribe somebody else.
    /// </summary>
    public string? UserReference { get; private set; }

    public void SetUserReference(string? userReference) => UserReference = userReference;
}
