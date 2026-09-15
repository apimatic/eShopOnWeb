namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a capture fails because the authorization hold has gone stale/expired. The
/// fulfilment flow catches this to attempt a reauthorization rather than failing outright.
/// </summary>
public class PayPalAuthorizationExpiredException : PaymentException
{
    public PayPalAuthorizationExpiredException(string message) : base(message)
    {
    }
}
