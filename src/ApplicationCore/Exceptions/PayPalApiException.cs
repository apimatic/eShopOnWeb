using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call fails. Carries the machine-readable issue name and
/// PayPal debug id so callers (and operators) can act on it.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? IssueName { get; }
    public string? DebugId { get; }

    public PayPalApiException(string message, int statusCode, string? issueName = null, string? debugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        IssueName = issueName;
        DebugId = debugId;
    }

    /// <summary>True when the failure indicates the authorization can no longer be
    /// captured or renewed (expired / voided) — an operator must act on it.</summary>
    public bool IsAuthorizationUnusable =>
        string.Equals(IssueName, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "AUTH_CAPTURE_CURRENCY_MISMATCH", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "MAX_CAPTURE_COUNT_EXCEEDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a capture failed because the authorization is stale/expired and
    /// should be reauthorized before retrying. PayPal has no single documented issue name
    /// for this, so we match any "…EXPIRED" issue reported on the capture.</summary>
    public bool IsAuthorizationExpired =>
        IssueName is not null && IssueName.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase);
}
