using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the PayPal gateway boundary raises for everything that goes wrong talking
/// to PayPal: a provider error response, a transport failure, or an unreadable body. Carries the
/// caller-safe message, the provider HTTP status where one was returned, and PayPal's own correlation
/// id (<c>debug_id</c>) where present, so operators can act on it and correlate with PayPal.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, HttpStatusCode? statusCode = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, when the failure was a provider response.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>PayPal's internal correlation id (<c>debug_id</c>), when the error body carried one.</summary>
    public string? DebugId { get; }

    /// <summary>
    /// True when PayPal reported the authorization can no longer be captured or renewed (e.g. it has
    /// expired past the reauthorization window). Fulfilment surfaces this in operator-actionable terms.
    /// </summary>
    public bool AuthorizationUnrenewable { get; init; }

    /// <summary>
    /// True when a capture failed specifically because the authorization has gone stale/expired but may
    /// still be renewable — the signal fulfilment uses to re-authorize rather than fail outright.
    /// </summary>
    public bool AuthorizationExpired { get; init; }

    /// <summary>The PayPal issue code from the error body (e.g. AUTHORIZATION_EXPIRED), when present.</summary>
    public string? Issue { get; init; }
}
