using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>API handle of the plan (Maxio product) to subscribe to, e.g. from GET /api/subscription-plans.</summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>The authenticated caller, populated from the JWT by the route handler.</summary>
    [JsonIgnore]
    public ClaimsPrincipal? User { get; set; }
}
