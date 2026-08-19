namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Maxio product handle to subscribe to (for example, the family's default plan).</summary>
    public string ProductHandle { get; set; } = string.Empty;
}
