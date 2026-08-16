using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure that came back from (or while talking to) the payment provider. Carries a
/// caller-safe message and the HTTP status the API boundary should surface — a provider
/// rejection the caller can act on (4xx) is kept distinct from a provider outage (5xx).
/// Never carries raw provider/SDK exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the API should return for this failure.</summary>
    public int StatusCode { get; }
}
