namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of one of the plans returned by GET /api/subscription-plans.</summary>
    public string? ProductHandle { get; set; }
}
