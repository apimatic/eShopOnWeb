using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;

/// <summary>
/// Thrown when a PayPal API call returns an error. Carries the fields from PayPal's error model
/// (name, message, debug_id and per-issue details) so callers can surface something actionable.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string name, string message, string? debugId, IReadOnlyList<string> issues)
        : base(message)
    {
        StatusCode = statusCode;
        Name = name;
        DebugId = debugId;
        Issues = issues;
    }

    public int StatusCode { get; }
    public string Name { get; }
    public string? DebugId { get; }
    public IReadOnlyList<string> Issues { get; }

    /// <summary>True when the failure is because the authorization can no longer be captured as-is.</summary>
    public bool IsAuthorizationExpired => Mentions("AUTHORIZATION_EXPIRED") || Mentions("AUTH_CAPTURE_AUTHORIZATION_EXPIRED");

    private bool Mentions(string issue) =>
        string.Equals(Name, issue, StringComparison.OrdinalIgnoreCase) ||
        (Issues is not null && Issues.Any(i => string.Equals(i, issue, StringComparison.OrdinalIgnoreCase)));
}
