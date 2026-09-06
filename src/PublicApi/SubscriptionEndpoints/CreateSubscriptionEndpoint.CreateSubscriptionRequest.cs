using Microsoft.eShopWeb.MaxioBilling.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted the configured default plan is used.
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The caller, taken from the bearer token rather than the request body. Internal so it is
    /// neither model-bound from the payload nor published in the API schema.
    /// </summary>
    internal SubscriberIdentity? Subscriber { get; set; }
}
