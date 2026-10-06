namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated shopper to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to enroll in (see GET api/subscription-plans).
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Optional first name for the billing customer record; used only when the
    /// shopper's Maxio customer is created on first subscribe. Defaults to the
    /// email local part.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional last name for the billing customer record; same conditions as
    /// <see cref="FirstName"/>. Defaults to "Shopper".
    /// </summary>
    public string? LastName { get; set; }
}
