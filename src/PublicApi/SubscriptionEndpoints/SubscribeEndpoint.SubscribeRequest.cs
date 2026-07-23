using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to enroll in, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the bearer token, not the request body — a caller cannot subscribe on
    /// someone else's behalf.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
