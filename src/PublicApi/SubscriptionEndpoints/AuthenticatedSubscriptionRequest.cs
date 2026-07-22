namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Base for subscription requests that act on behalf of the signed-in user.
/// </summary>
public abstract class AuthenticatedSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The caller's stable identity. The setter is deliberately not public: this is always taken from the
    /// authenticated principal and can never be supplied by the caller.
    /// </summary>
    public string UserReference { get; internal set; } = string.Empty;
}
