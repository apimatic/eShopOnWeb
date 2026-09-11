using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the payment gateway when a capture is attempted against an authorization PayPal
/// reports as expired. The fulfilment flow catches this to renew the authorization and retry,
/// rather than failing the fulfilment outright. It deliberately does not derive from
/// <see cref="PaymentException"/> so the renew-or-fail logic can distinguish it.
/// </summary>
public class AuthorizationExpiredException : Exception
{
    public AuthorizationExpiredException(string message) : base(message) { }

    public AuthorizationExpiredException(string message, Exception innerException)
        : base(message, innerException) { }
}
