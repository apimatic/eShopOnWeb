using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed. <see cref="StatusCode"/> is the HTTP status the API boundary should
/// return: a provider rejection the caller can act on (a decline or invalid card) maps to a 4xx,
/// while an unreachable/unreadable provider maps to a 5xx. The message is always caller-safe —
/// SDK/framework exception text is never propagated onto the wire.
/// </summary>
public class PaymentException : Exception
{
    public int StatusCode { get; }

    public PaymentException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>
/// The provider rejected the payment for a reason the caller can act on — a declined card, an
/// invalid card, or a validation error. Surfaced as HTTP 422 so a retry of the same request is
/// pointless without changing the input.
/// </summary>
public class PaymentDeclinedException : PaymentException
{
    public PaymentDeclinedException(string message, Exception? innerException = null)
        : base(message, 422, innerException)
    {
    }
}

/// <summary>
/// The provider was unreachable, timed out, or returned something that could not be processed.
/// Surfaced as HTTP 502 — the outcome is unknown to us, not a rejection of the caller's input.
/// </summary>
public class PaymentProviderUnavailableException : PaymentException
{
    public PaymentProviderUnavailableException(string message, Exception? innerException = null)
        : base(message, 502, innerException)
    {
    }
}
