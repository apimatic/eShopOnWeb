using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment processor (PayPal) rejects a request. Carries the fine-grained issue code
/// and PayPal's debug id so operators can act on it, and so callers of fulfilment can decide whether a
/// stale authorization should be renewed.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? issue = null, string? debugId = null, int? statusCode = null)
        : base(message)
    {
        Issue = issue;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    /// <summary>The fine-grained, application-level error code PayPal returned (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's internal correlation id for the failing call.</summary>
    public string? DebugId { get; }

    /// <summary>The HTTP status code PayPal returned.</summary>
    public int? StatusCode { get; }
}
