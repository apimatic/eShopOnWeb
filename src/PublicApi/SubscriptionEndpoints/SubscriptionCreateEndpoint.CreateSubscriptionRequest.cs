namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Body of POST /api/subscriptions. The subscriber is always taken from the bearer token, never from here.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by GET /api/subscription-plans.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional. Used only when a billing customer has to be created for this shopper; when omitted, a name
    /// is derived from the account's user name.
    /// </summary>
    public string? FirstName { get; set; }

    /// <inheritdoc cref="FirstName"/>
    public string? LastName { get; set; }
}
