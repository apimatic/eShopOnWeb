namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated caller to a plan.
/// Only <see cref="PlanHandle"/> is client-supplied; the identity fields are always populated
/// server-side from the JWT and overwrite anything sent in the body.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to. Optional; defaults to the first plan in the family.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Set server-side from the authenticated identity. Ignored if supplied by the client.</summary>
    public string UserReference { get; set; } = string.Empty;

    /// <summary>Set server-side from the authenticated identity. Ignored if supplied by the client.</summary>
    public string Email { get; set; } = string.Empty;
}
