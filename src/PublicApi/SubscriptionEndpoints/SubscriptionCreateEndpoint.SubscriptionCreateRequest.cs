using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    public string? AuthenticatedUsername { get; set; }
}
