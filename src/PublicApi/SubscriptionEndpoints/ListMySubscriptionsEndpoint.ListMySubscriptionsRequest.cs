using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>The authenticated caller, populated from the JWT by the route handler.</summary>
    [JsonIgnore]
    public ClaimsPrincipal? User { get; set; }
}
