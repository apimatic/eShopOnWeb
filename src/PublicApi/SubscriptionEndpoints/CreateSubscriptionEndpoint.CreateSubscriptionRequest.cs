namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Maxio product handle to subscribe to. When omitted, the first plan in the configured family is used.
    /// </summary>
    public string? ProductHandle { get; set; }
}
