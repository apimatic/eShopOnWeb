namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; init; }
    public string TargetProductHandle { get; init; } = string.Empty;
    public string UserReference { get; init; } = string.Empty;

    public PreviewPlanChangeRequest()
    {
    }

    public PreviewPlanChangeRequest(int subscriptionId, string targetProductHandle, string userReference)
    {
        SubscriptionId = subscriptionId;
        TargetProductHandle = targetProductHandle;
        UserReference = userReference;
    }
}
