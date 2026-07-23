namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Whose subscriptions to list. Ignored for non-administrators.</summary>
    public string? UserReference { get; set; }
}
