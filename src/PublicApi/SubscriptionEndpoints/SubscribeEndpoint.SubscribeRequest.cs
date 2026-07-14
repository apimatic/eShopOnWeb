namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>Populated by the route handler from the authenticated caller; not caller-supplied.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Populated by the route handler from the authenticated caller; not caller-supplied.</summary>
    public string Email { get; set; } = string.Empty;
}
