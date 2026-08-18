using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure reported by (or reaching) the PayPal gateway. Carries a caller-safe message and the
/// HTTP status the API boundary should surface — a provider 4xx the caller can act on maps back to
/// that same 4xx; a transport or unknown failure maps to 502.
/// </summary>
public class PaymentGatewayException : Exception
{
    /// <summary>Status code the public API should return to its caller.</summary>
    public int ClientStatusCode { get; }

    public PaymentGatewayException(string message, int clientStatusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        ClientStatusCode = clientStatusCode;
    }
}
