namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan. The caller's identity comes from the
/// bearer token; <see cref="UserName"/> is populated server-side from the token, never from the body.
/// <see cref="FirstName"/> / <see cref="LastName"/> are optional and only used when Maxio must first
/// create a customer for this shopper.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }

    /// <summary>Set by the endpoint from the authenticated token; never accept from the client.</summary>
    public string UserName { get; set; }
}
