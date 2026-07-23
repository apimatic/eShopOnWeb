using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The durable handle of the plan to enroll in (e.g. <c>eshop-pro</c>).</summary>
    [Required]
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Taken from the access token, never from the request body — a caller cannot subscribe on
    /// somebody else's behalf.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;
}
