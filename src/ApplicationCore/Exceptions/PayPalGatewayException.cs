using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure reported by (or reaching) the PayPal gateway. Carries the provider's own error
/// identity — issue name, human message and correlation/debug id — so an operator can act on it,
/// and the HTTP status the API boundary should surface (a PayPal outage/credential problem is our
/// fault → 502; a caller-fixable rejection is surfaced with its own status).
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(
        string message,
        string? issue = null,
        string? debugId = null,
        int statusCode = 502) : base(message)
    {
        Issue = issue;
        DebugId = debugId;
        StatusCode = statusCode;
    }

    /// <summary>PayPal's machine-readable issue/name, when present (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? Issue { get; }

    /// <summary>PayPal's correlation id (debug_id) for support/log correlation.</summary>
    public string? DebugId { get; }

    /// <summary>The HTTP status code this failure maps to at the API boundary.</summary>
    public int StatusCode { get; }
}
