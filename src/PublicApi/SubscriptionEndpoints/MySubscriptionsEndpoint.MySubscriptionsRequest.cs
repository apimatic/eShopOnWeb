namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest : BaseRequest
{
    /// <summary>Server-assigned from the authenticated principal — never bound from client input.</summary>
    public string? UserId { get; set; }
}
