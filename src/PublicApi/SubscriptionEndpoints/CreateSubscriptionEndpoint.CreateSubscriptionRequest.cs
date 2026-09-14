using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Stable handle of the plan to subscribe to, e.g. "eshop-pro". From the request body.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The authenticated user's identity, populated from the JWT by the endpoint. Ignored on
    /// deserialization so callers cannot supply it in the request body and impersonate another user.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
