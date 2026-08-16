using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at fulfilment when the payment hold has gone stale and can no longer be renewed
/// (reauthorized), so the capture cannot proceed. The message is phrased for an operator to act on:
/// the order needs to be re-paid by the shopper.
/// </summary>
public class AuthorizationNotRenewableException : Exception
{
    public AuthorizationNotRenewableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
