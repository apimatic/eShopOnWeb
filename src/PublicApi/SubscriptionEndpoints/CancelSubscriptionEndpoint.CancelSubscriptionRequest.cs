namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; init; }
    public bool EndOfPeriod { get; init; }
    public string? Reason { get; init; }
    public string UserReference { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }

    public CancelSubscriptionRequest()
    {
    }

    public CancelSubscriptionRequest(int subscriptionId, bool endOfPeriod, string? reason, string userReference, bool isAdmin)
    {
        SubscriptionId = subscriptionId;
        EndOfPeriod = endOfPeriod;
        Reason = reason;
        UserReference = userReference;
        IsAdmin = isAdmin;
    }
}
