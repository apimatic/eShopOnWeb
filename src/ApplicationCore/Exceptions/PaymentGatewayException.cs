using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal API call fails. Carries the parts of PayPal's error model an operator or
/// the calling code can act on, without leaking PayPal wire types out of the infrastructure layer.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        int statusCode,
        string? errorName = null,
        string? issue = null,
        string? debugId = null,
        IReadOnlyList<string>? issues = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Issue = issue;
        DebugId = debugId;
        Issues = issues ?? Array.Empty<string>();
    }

    /// <summary>HTTP status code PayPal returned.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's top-level error name (e.g. UNPROCESSABLE_ENTITY).</summary>
    public string? ErrorName { get; }

    /// <summary>The first fine-grained issue code (e.g. AUTHORIZATION_EXPIRED), if any.</summary>
    public string? Issue { get; }

    /// <summary>PayPal correlation id, for support/debugging.</summary>
    public string? DebugId { get; }

    /// <summary>All fine-grained issue codes reported.</summary>
    public IReadOnlyList<string> Issues { get; }
}
