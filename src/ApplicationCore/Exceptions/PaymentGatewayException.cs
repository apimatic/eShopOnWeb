using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the payment-gateway boundary. Carries a caller-safe message plus the HTTP status the
/// API should surface: a provider 4xx (the caller can act on it — bad card, over-refund, an
/// authorization that can no longer be renewed) maps back to a client 4xx; a transport failure,
/// an unreadable body or an unknown error maps to 5xx. Never carries provider/SDK exception text.
/// </summary>
public class PaymentGatewayException : Exception, IApiStatusCodeException
{
    public PaymentGatewayException(string message, int statusCode, string? issue = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    public PaymentGatewayException(string message, int statusCode, Exception inner, string? issue = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        Issue = issue;
    }

    /// <summary>HTTP status the API should return to its own caller.</summary>
    public int StatusCode { get; }

    /// <summary>Machine-readable provider reason (PayPal's <c>issue</c>), when one was available.</summary>
    public string? Issue { get; }
}
