using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// How a payment-processor failure should be treated. The distinction is what stops every provider
/// failure collapsing into one opaque 500: our own credential or quota problems are not the caller's
/// fault, and must not be reported to them as if they were.
/// </summary>
public enum PaymentGatewayFailure
{
    /// <summary>Something the caller sent was rejected — a bad card, a malformed request.</summary>
    Rejected,

    /// <summary>The state at the processor does not allow the operation (e.g. already captured).</summary>
    Conflict,

    /// <summary>Our credentials, our quota, or the processor being down. Never the caller's fault.</summary>
    Unavailable,

    /// <summary>
    /// The processor wants the shopper to approve the payment in a browser. This integration is
    /// server-to-server and has no approval round-trip, so it is surfaced rather than worked around.
    /// </summary>
    ApprovalRequired,

    /// <summary>
    /// The request may or may not have taken effect — a transport failure after the bytes went out,
    /// or a response we could not read. Reconciliation settles it; a retry must not assume "failed".
    /// </summary>
    OutcomeUnknown
}

/// <summary>
/// The single failure type the application sees from the payment gateway, whatever went wrong
/// underneath. Carries the processor's own identifiers so a support conversation is possible.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, PaymentGatewayFailure kind,
        string? providerCode = null, string? debugId = null,
        HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderCode = providerCode;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    public PaymentGatewayFailure Kind { get; }

    /// <summary>The processor's own error name/code, where it gave one.</summary>
    public string? ProviderCode { get; }

    /// <summary>PayPal's <c>debug_id</c> — the correlation id their support asks for.</summary>
    public string? DebugId { get; }

    public HttpStatusCode? StatusCode { get; }
}
