using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A PayPal REST API call returned an error. Carries the details needed to branch on the failure
/// (HTTP status, PayPal's issue name) and to support-escalate (debug id), without leaking the raw
/// response to callers.
/// </summary>
public class PayPalApiException : Exception
{
    public int StatusCode { get; }
    public string? IssueName { get; }
    public string? DebugId { get; }

    public PayPalApiException(int statusCode, string? issueName, string? debugId, string message)
        : base(message)
    {
        StatusCode = statusCode;
        IssueName = issueName;
        DebugId = debugId;
    }

    /// <summary>True when PayPal reports the authorization can no longer be captured (expired/voided).</summary>
    public bool IndicatesAuthorizationNoLongerCapturable =>
        string.Equals(IssueName, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "AUTHORIZATION_VOIDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "INVALID_AUTHORIZATION_ID", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "PREVIOUSLY_CAPTURED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(IssueName, "PREVIOUSLY_VOIDED", StringComparison.OrdinalIgnoreCase);
}
