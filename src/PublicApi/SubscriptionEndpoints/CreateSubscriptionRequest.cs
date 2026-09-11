namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;
    public string UserReference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public System.Threading.CancellationToken CancellationToken { get; set; } = default;
}
