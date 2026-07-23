using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Base for subscription requests that act on behalf of the authenticated caller.
/// </summary>
public abstract class AuthenticatedSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The stable eShopOnWeb user reference. Deliberately excluded from the request body and set
    /// only from the bearer token after binding, so a caller cannot name another user.
    /// </summary>
    [JsonIgnore]
    public string UserReference { get; set; }
}
