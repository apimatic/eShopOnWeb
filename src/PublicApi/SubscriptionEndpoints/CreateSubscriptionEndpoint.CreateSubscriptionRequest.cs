using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request body for <c>POST /api/subscriptions</c>. Only <see cref="PlanHandle"/> is bound from the
/// body; the subscriber identity is resolved from the JWT by the endpoint (never from the body) and
/// is marked <see cref="JsonIgnoreAttribute"/> so it cannot be spoofed by the caller.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, e.g. <c>eshop-pro</c>.</summary>
    public string? PlanHandle { get; set; }

    [JsonIgnore]
    public string UserReference { get; set; } = string.Empty;

    [JsonIgnore]
    public string Email { get; set; } = string.Empty;

    [JsonIgnore]
    public string FirstName { get; set; } = string.Empty;

    [JsonIgnore]
    public string LastName { get; set; } = string.Empty;
}
