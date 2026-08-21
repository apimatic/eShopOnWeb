using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A PayPal call could not be completed. The message is operator-safe (built from PayPal's own error
/// message and issue codes, never an internal type name). <see cref="StatusCode"/> tells the API boundary
/// how to surface it: a provider 4xx (validation, decline, conflict — the caller can act on it) is
/// carried as a client 4xx, while a transport failure, a 5xx or an unreadable body becomes 502.
/// </summary>
public class PayPalProviderException : Exception
{
    public const int DefaultStatusCode = 502; // Bad Gateway — provider failure the caller cannot fix

    public PayPalProviderException(string message, int statusCode = DefaultStatusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status the API boundary should surface for this failure.</summary>
    public int StatusCode { get; }
}
