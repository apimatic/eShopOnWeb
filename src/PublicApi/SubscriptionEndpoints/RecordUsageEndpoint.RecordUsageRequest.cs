namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public string ActingBuyerId { get; init; }
    public bool IsAdmin { get; init; }
    public int SubscriptionId { get; init; }
    public double Quantity { get; init; }
    public string? Memo { get; init; }

    public RecordUsageRequest(string actingBuyerId, bool isAdmin, int subscriptionId, double quantity, string? memo)
    {
        ActingBuyerId = actingBuyerId;
        IsAdmin = isAdmin;
        SubscriptionId = subscriptionId;
        Quantity = quantity;
        Memo = memo;
    }
}
