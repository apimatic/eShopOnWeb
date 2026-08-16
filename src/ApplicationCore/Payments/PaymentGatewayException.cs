using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when a PayPal API call fails. Carries enough of PayPal's error detail
/// (HTTP status, issue codes, debug id) for callers to react — for example, to
/// detect an expired authorization and reauthorize instead of failing a fulfilment.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int statusCode, string? debugId, IReadOnlyList<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        DebugId = debugId;
        Issues = issues ?? new List<string>();
    }

    /// <summary>HTTP status code returned by PayPal.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal debug id, required when contacting PayPal support.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal issue codes (e.g. AUTHORIZATION_EXPIRED, INSTRUMENT_DECLINED).</summary>
    public IReadOnlyList<string> Issues { get; }

    public bool HasIssue(string issue) =>
        Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the failure indicates the authorization can no longer be captured as-is.</summary>
    public bool IsAuthorizationExpired =>
        HasIssue("AUTHORIZATION_EXPIRED") || HasIssue("AUTH_EXPIRED");
}
