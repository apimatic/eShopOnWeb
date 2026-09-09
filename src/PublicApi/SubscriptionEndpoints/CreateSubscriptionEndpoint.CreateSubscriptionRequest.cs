using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, chosen from GET /api/subscription-plans
    /// (e.g. "eshop-pro").
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The enrolling user, resolved server-side from the JWT — never bound from the request body.
    /// </summary>
    [JsonIgnore]
    public BillingCustomer? Caller { get; set; }
}
