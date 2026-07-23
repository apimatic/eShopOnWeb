using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enroll in, e.g. "eshop-pro".</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Administrators only: the user to act on behalf of. Ignored for other callers.</summary>
    public string? OnBehalfOfUserName { get; set; }

    /// <summary>Resolved from the bearer token; never supplied by the caller.</summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
