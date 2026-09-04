namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when the authorization that backs an order can no longer be
/// renewed. The operator must ask the shopper to pay again rather than retrying.
/// </summary>
public class AuthorizationCannotBeRenewedException : ApiException
{
    public AuthorizationCannotBeRenewedException(string message)
        : base(message, 409)
    {
    }
}