using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed. Carries the provider's HTTP status (when there was one) plus the
/// provider's own error name and correlation id, so the API boundary can map "the caller sent
/// something invalid" (a 4xx passed through) apart from "the processor is unavailable" (our
/// credentials/quota, or transport — surfaced as 5xx), without leaking internal detail.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message, HttpStatusCode? statusCode = null,
        string? providerCode = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        ProviderCode = providerCode;
        DebugId = debugId;
    }

    /// <summary>Provider HTTP status, when the provider answered.</summary>
    public HttpStatusCode? StatusCode { get; }
    /// <summary>Provider error name/code (e.g. from PayPal's typed error body).</summary>
    public string? ProviderCode { get; }
    /// <summary>Provider correlation id (PayPal debug_id) for support/log correlation.</summary>
    public string? DebugId { get; }
}

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser. Per the integration mandate we do not build an approval round-trip — we stop and
/// surface it so an operator can act.
/// </summary>
public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message, Exception? inner = null)
        : base(message, HttpStatusCode.Conflict, inner: inner) { }
}
