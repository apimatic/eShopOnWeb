using System.Text.Json.Serialization;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, as returned by GET api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional first name to register with the billing provider.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional last name to register with the billing provider.</summary>
    public string? LastName { get; set; }

    /// <summary>Optional organization to register with the billing provider.</summary>
    public string? Organization { get; set; }

    /// <summary>
    /// The caller, resolved from the bearer token by the route handler. Never bound from the body.
    /// </summary>
    [JsonIgnore]
    public SubscriberIdentity? Subscriber { get; set; }
}
