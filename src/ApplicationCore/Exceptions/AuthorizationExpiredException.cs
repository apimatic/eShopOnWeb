namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A capture failed specifically because the authorization had gone stale/expired. Distinct from a
/// generic gateway failure so the fulfilment flow can attempt to renew the hold rather than failing
/// outright. Thrown by the gateway when PayPal reports an expiry-type issue on capture.
/// </summary>
public class AuthorizationExpiredException : PayPalGatewayException
{
    public AuthorizationExpiredException(string message) : base(message) { }
}
