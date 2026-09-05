namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The Maxio product handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional. Used only the first time a Maxio customer is created for this user.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional. Used only the first time a Maxio customer is created for this user.</summary>
    public string? LastName { get; set; }
}
