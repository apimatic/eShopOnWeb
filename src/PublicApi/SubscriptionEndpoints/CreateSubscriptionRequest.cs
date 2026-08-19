namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Product API handle of the plan to subscribe to. When omitted, the first
    /// product in the configured Maxio product family is used.
    /// </summary>
    public string? ProductHandle { get; set; }
}
