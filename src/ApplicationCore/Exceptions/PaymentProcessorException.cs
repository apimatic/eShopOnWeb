using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type raised at the payment boundary. Carries a caller-safe message and the
/// HTTP status the API surface should return, so distinct provider failures stay distinct
/// (a provider 4xx the caller can act on maps to a client 4xx; an outage/transport/parse
/// failure maps to 5xx). Provider internals (debug ids, raw bodies) are logged, never surfaced.
/// </summary>
public class PaymentProcessorException : Exception
{
    public PaymentProcessorException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status the API boundary should return for this failure.</summary>
    public int StatusCode { get; }
}
