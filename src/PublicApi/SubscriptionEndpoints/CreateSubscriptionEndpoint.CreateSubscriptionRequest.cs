namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name; derived from the account email when omitted.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name; derived from the account email when omitted.</summary>
    public string? LastName { get; set; }
}
