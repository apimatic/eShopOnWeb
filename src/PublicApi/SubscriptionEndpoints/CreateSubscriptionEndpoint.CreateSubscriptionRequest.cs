namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (from GET /api/subscription-plans). Required.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional given name for the billing customer; derived from the email when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional family name for the billing customer; defaulted when omitted.</summary>
    public string? LastName { get; set; }
}
