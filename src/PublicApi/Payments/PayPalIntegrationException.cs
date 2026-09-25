using System;
using System.Net;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The single failure type the PayPal gateway raises. Every SDK failure (a typed API error, a raw
/// error, an unreadable body, a connection failure or timeout, or a credential that could not be
/// applied) is translated to this type at the gateway boundary so callers handle one failure kind.
/// </summary>
public class PayPalIntegrationException : Exception
{
    /// <summary>The HTTP status PayPal returned, when the provider actually answered.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>PayPal's own correlation id (<c>debug_id</c>) from the error body, when present.</summary>
    public string? DebugId { get; }

    /// <summary>
    /// True when the write may have taken effect at PayPal but the outcome could not be confirmed
    /// (a connection failure after the request may have been received). The caller must treat this
    /// as unknown, not failed.
    /// </summary>
    public bool OutcomeUnknown { get; }

    public PayPalIntegrationException(string message, HttpStatusCode? statusCode = null,
        string? debugId = null, Exception? innerException = null, bool outcomeUnknown = false)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        OutcomeUnknown = outcomeUnknown;
    }
}

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (e.g. 3-D Secure / PAYER_ACTION_REQUIRED). Per the integration's scope this is
/// surfaced, not worked around — no browser approval round-trip is built.
/// </summary>
public sealed class PayerActionRequiredException : PayPalIntegrationException
{
    public PayerActionRequiredException(string message, HttpStatusCode? statusCode = null, string? debugId = null)
        : base(message, statusCode, debugId)
    {
    }
}
