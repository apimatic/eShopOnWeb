using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when a hold has gone stale and can no longer be renewed
/// (PayPal no longer allows re-authorization). The message is phrased so an operator
/// can act on it — the order must be re-placed and re-paid rather than fulfilled.
/// </summary>
public class AuthorizationNotRenewableException : PaymentProcessorException
{
    public AuthorizationNotRenewableException(string message, Exception? innerException = null)
        : base(message, statusCode: 409, innerException)
    {
    }
}
