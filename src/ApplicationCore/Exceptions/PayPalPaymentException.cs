using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The application's single failure type for anything that goes wrong talking to PayPal —
/// an API error, a transport failure, or an unreadable response. Carries PayPal's own
/// correlation id (<c>debug_id</c>) and, where known, the HTTP status, so the API boundary
/// can present a coherent, non-leaky error and logs can be correlated with PayPal.
/// </summary>
public class PayPalPaymentException : Exception
{
    public PayPalPaymentException(
        string message,
        string? debugId = null,
        HttpStatusCode? statusCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        DebugId = debugId;
        StatusCode = statusCode;
    }

    /// <summary>PayPal's internal correlation id from the error body, when present.</summary>
    public string? DebugId { get; }

    /// <summary>The HTTP status PayPal returned, when it could be read.</summary>
    public HttpStatusCode? StatusCode { get; }
}
