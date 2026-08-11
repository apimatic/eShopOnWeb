using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the gateway when a capture fails because the authorization has gone
/// stale. It signals the fulfilment flow to renew the hold and try again, rather
/// than failing fulfilment outright.
/// </summary>
public class StaleAuthorizationException : PaymentException
{
    public StaleAuthorizationException(string message, Exception? innerException = null)
        : base(message, 409, innerException!)
    {
    }
}
