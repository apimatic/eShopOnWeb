using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure reported by (or reaching) the payment provider. Raised at the single gateway boundary that
/// wraps the PayPal SDK, so the rest of the application has one failure type to handle. Carries a
/// caller-safe message plus the discriminators needed to map it to an HTTP status: the provider's HTTP
/// status (when available), its correlation id (<see cref="DebugId"/>), and the first issue code.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, HttpStatusCode? statusCode = null,
        string? debugId = null, string? issueCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        IssueCode = issueCode;
    }

    /// <summary>The provider HTTP status, when the failure carried one.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>PayPal's <c>debug_id</c> correlation id, for support/log correlation.</summary>
    public string? DebugId { get; }

    /// <summary>The provider's first issue code (e.g. <c>AUTHORIZATION_EXPIRED</c>), when present.</summary>
    public string? IssueCode { get; }

    /// <summary>
    /// True when the failure indicates the authorization is no longer capturable and must be renewed —
    /// PayPal reports this on capture as the issue <c>AUTHORIZATION_EXPIRED</c>.
    /// </summary>
    public bool IsAuthorizationExpired =>
        IssueCode is not null && IssueCode.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
}
