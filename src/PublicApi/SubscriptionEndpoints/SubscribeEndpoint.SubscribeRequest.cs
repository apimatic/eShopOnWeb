using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. "eshop-pro".</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// The subscriber's identity, populated server-side from the JWT (never from the request
    /// body). Ignored for (de)serialization so a client cannot spoof another user.
    /// </summary>
    [JsonIgnore]
    public string? UserName { get; set; }
}
