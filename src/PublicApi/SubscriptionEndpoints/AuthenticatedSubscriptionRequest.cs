using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Base for subscription requests that act on behalf of the authenticated caller.
/// </summary>
/// <remarks>
/// The caller's identity is carried on the per-request object rather than on the endpoint instance,
/// because endpoints are shared across concurrent requests. It is marked <see cref="JsonIgnoreAttribute"/>
/// so it can only ever be set from the bearer token — a client cannot supply or spoof it in the body.
/// </remarks>
public abstract class AuthenticatedSubscriptionRequest : BaseRequest
{
    [JsonIgnore]
    public string? AuthenticatedUserName { get; set; }

    /// <summary>True when the caller holds the administrators role and may act on any subscription.</summary>
    [JsonIgnore]
    public bool IsAdministrator { get; set; }
}
