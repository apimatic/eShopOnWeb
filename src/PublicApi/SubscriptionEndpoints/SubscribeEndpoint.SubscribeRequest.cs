namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Server-assigned from the authenticated principal — never bound from client input.</summary>
    public string? UserId { get; set; }
}
