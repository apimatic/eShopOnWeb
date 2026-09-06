namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public int ProductId { get; init; }
    public string? UserId { get; set; }
    public string? Email { get; set; }
}
