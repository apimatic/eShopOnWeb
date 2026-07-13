namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageBody
{
    public double Quantity { get; set; }
    public string? Memo { get; set; }
}

public class RecordUsageRequest : BaseRequest
{
    public RecordUsageRequest(long subscriptionId, string customerReference, bool actingAsAdmin, RecordUsageBody body)
    {
        SubscriptionId = subscriptionId;
        CustomerReference = customerReference;
        ActingAsAdmin = actingAsAdmin;
        Quantity = body.Quantity;
        Memo = body.Memo;
    }

    public long SubscriptionId { get; }
    public string CustomerReference { get; }
    public bool ActingAsAdmin { get; }
    public double Quantity { get; }
    public string? Memo { get; }
}
