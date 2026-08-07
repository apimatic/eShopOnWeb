using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the PayPal API fails (declined card, validation error,
/// or an upstream failure). <see cref="StatusCode"/> carries the HTTP status the
/// API surface should return to the caller. Messages never contain card details.
/// </summary>
public class PayPalException : Exception
{
    /// <summary>The HTTP status the PublicApi should return for this failure.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's debug id, when available, to correlate with PayPal's logs.</summary>
    public string? DebugId { get; }

    public PayPalException(string message, int statusCode = 502, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }
}
