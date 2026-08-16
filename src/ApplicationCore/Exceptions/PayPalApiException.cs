using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call returns a non-success status. Carries enough detail
/// (HTTP status, PayPal issue name, debug_id) for callers to react and for support.
/// The raw request body (which may contain card data) is never included here.
/// </summary>
public class PayPalApiException : Exception
{
    public int HttpStatus { get; }

    /// <summary>PayPal issue name from details[0].issue, if any (e.g. AUTHORIZATION_EXPIRED).</summary>
    public string? Issue { get; }

    /// <summary>PayPal debug_id, required when contacting PayPal support.</summary>
    public string? DebugId { get; }

    public PayPalApiException(string message, int httpStatus, string? issue, string? debugId)
        : base(message)
    {
        HttpStatus = httpStatus;
        Issue = issue;
        DebugId = debugId;
    }

    /// <summary>
    /// True when a capture failed because the authorization can no longer be captured as-is
    /// but might be renewable (expired / voided / pending state).
    /// </summary>
    public bool IsAuthorizationStale =>
        Issue is "AUTHORIZATION_EXPIRED"
            or "AUTH_CAPTURE_CURRENCY_MISMATCH" // defensive; not stale but surfaced clearly
            or "PAYMENT_STATE_INVALID"
            or "AUTHORIZATION_VOIDED"
            or "INVALID_AUTHORIZATION_ID_STATE";

    /// <summary>True when PayPal indicates the authorization cannot be renewed/reauthorized.</summary>
    public bool IsReauthorizationRefused =>
        Issue is "REAUTHORIZATION_NOT_ALLOWED"
            or "AUTHORIZATION_ALREADY_CAPTURED"
            or "MAX_NUMBER_OF_PAYMENT_ATTEMPTS_EXCEEDED"
            or "REAUTHORIZATION_TOO_SOON"
            or "AUTHORIZATION_EXPIRED";
}
