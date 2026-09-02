using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization hold on the shopper's funds has gone stale and PayPal can no longer
/// renew it. The operator must ask the shopper to pay again so a fresh hold is placed.
/// </summary>
public class AuthorizationNotRenewableException : PaymentGatewayException
{
    public AuthorizationNotRenewableException(string message, string? debugId = null)
        : base(HttpStatusCode.Conflict, "AUTHORIZATION_NOT_RENEWABLE", message, debugId)
    {
    }
}
