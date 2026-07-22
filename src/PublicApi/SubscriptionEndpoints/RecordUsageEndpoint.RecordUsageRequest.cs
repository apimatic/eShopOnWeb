namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class RecordUsageRequest : BaseRequest
{
    public decimal Quantity { get; set; }

    public string? Memo { get; set; }

    /// <summary>Set from the route, never from the request body.</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Set from the bearer token; <c>null</c> for administrators.</summary>
    public string? OwnerBuyerId { get; set; }
}
