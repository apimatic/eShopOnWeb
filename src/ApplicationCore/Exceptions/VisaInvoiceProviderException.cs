using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a call to the Visa invoicing provider fails. It carries the
/// provider's own reason and status so callers can tell a legitimately refused
/// transition (a bill's state disallows it) from a genuine integration fault.
/// The message never contains any credential value.
/// </summary>
public class VisaInvoiceProviderException : Exception
{
    public VisaInvoiceProviderException(string message, int? statusCode = null, string? reason = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Reason = reason;
    }

    /// <summary>The HTTP status code the provider returned, if any.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's machine-readable reason (for example ACTION_NOT_ALLOWED), if any.</summary>
    public string? Reason { get; }

    /// <summary>
    /// True when the provider refused because of the bill's current state (a bad
    /// request such as a not-allowed action) rather than an authentication,
    /// server, or connectivity fault.
    /// </summary>
    public bool IsStateConflict => StatusCode is >= 400 and < 500 and not 401 and not 403;
}
