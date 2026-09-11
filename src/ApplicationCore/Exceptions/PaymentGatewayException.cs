using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the payment processor (PayPal) rejects a request or is unreachable. Carries a
/// human-readable message plus the raw processor detail so operators can act on it.
/// </summary>
public class PaymentGatewayException : Exception
{
    /// <summary>PayPal error name/issue where available (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? ProcessorIssue { get; }

    /// <summary>HTTP status returned by the processor, if the failure was an HTTP response.</summary>
    public int? StatusCode { get; }

    public PaymentGatewayException(string message, string? processorIssue = null, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        ProcessorIssue = processorIssue;
        StatusCode = statusCode;
    }
}
