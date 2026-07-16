namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public string PreviewToken { get; set; } = string.Empty;

    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
