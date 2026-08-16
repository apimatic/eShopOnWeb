namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A capture was attempted against an authorization PayPal considers expired/stale. The payment
/// service reacts by trying to renew (reauthorize) the hold before failing the fulfilment.
/// </summary>
public class AuthorizationExpiredException : PaymentException
{
    public AuthorizationExpiredException(string message) : base(message) { }
}
