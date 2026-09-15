namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationRequest : BaseRequest
{
    public string? From { get; set; }
    public string? To { get; set; }
}
