namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public int SubscriptionId { get; init; }
    public int Quantity { get; init; }
    public string? Memo { get; init; }
    public string UserReference { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }

    public RecordUsageRequest()
    {
    }

    public RecordUsageRequest(int subscriptionId, int quantity, string? memo, string userReference, bool isAdmin)
    {
        SubscriptionId = subscriptionId;
        Quantity = quantity;
        Memo = memo;
        UserReference = userReference;
        IsAdmin = isAdmin;
    }
}
