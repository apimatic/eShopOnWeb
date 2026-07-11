namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionRequest : BaseRequest
{
    public string ActingBuyerId { get; init; }
    public bool IsAdmin { get; init; }
    public int SubscriptionId { get; init; }
    public bool EndOfPeriod { get; init; }
    public string? Reason { get; init; }

    public CancelSubscriptionRequest(string actingBuyerId, bool isAdmin, int subscriptionId, bool endOfPeriod, string? reason)
    {
        ActingBuyerId = actingBuyerId;
        IsAdmin = isAdmin;
        SubscriptionId = subscriptionId;
        EndOfPeriod = endOfPeriod;
        Reason = reason;
    }
}
