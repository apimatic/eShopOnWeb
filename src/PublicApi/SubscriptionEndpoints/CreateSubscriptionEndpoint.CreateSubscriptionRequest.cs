using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (from <c>GET /api/subscription-plans</c>).
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    // Billing identity, resolved server-side from the caller's token. Ignored on the
    // wire so a client cannot spoof another user's subscription.
    [JsonIgnore] public string Reference { get; set; } = string.Empty;
    [JsonIgnore] public string Email { get; set; } = string.Empty;
    [JsonIgnore] public string FirstName { get; set; } = string.Empty;
    [JsonIgnore] public string LastName { get; set; } = string.Empty;
}
