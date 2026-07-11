namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public string ActingBuyerId { get; init; }
    public bool IsAdmin { get; init; }
    public int SubscriptionId { get; init; }
    public string TargetProductHandle { get; init; }
    public bool Immediate { get; init; }

    public PreviewPlanChangeRequest(string actingBuyerId, bool isAdmin, int subscriptionId, string targetProductHandle, bool immediate)
    {
        ActingBuyerId = actingBuyerId;
        IsAdmin = isAdmin;
        SubscriptionId = subscriptionId;
        TargetProductHandle = targetProductHandle;
        Immediate = immediate;
    }
}
