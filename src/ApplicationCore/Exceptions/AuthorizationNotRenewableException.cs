namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when a stale authorization can no longer be renewed (re-authorized) with
/// PayPal — for example it has expired past PayPal's honor window. The message is written so an
/// operator can act on it (typically: the shopper must place and pay for a new order).
/// </summary>
public class AuthorizationNotRenewableException : PaymentException
{
    public AuthorizationNotRenewableException(string message) : base(message) { }
}
