namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by GET api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional; used only when the billing customer is first created. Defaults to the local part of the e-mail address.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional; used only when the billing customer is first created.</summary>
    public string? LastName { get; set; }
}
