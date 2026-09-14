using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The handle of the plan to subscribe to (e.g. <c>eshop-pro</c>).</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// The caller's Maxio identity, populated server-side from the JWT (never bound from the request body).
    /// </summary>
    [JsonIgnore]
    public MaxioCustomerIdentity? Identity { get; private set; }

    public void SetIdentity(MaxioCustomerIdentity identity) => Identity = identity;
}
