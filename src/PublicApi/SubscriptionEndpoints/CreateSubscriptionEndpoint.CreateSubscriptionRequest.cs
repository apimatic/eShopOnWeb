namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by GET api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional; used only when the shopper's billing customer is created.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional; used only when the shopper's billing customer is created.</summary>
    public string? LastName { get; set; }
}
