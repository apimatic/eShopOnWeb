using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal returned an error we cannot recover from at this layer (transport error or an unexpected
/// API response). Surfaced to the API as 502 Bad Gateway. The specific PayPal <see cref="Issue"/> is
/// carried when available so callers can reason about it.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int? httpStatus = null, string? issue = null, string? debugId = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        Issue = issue;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned, if the failure was an API error response.</summary>
    public int? HttpStatus { get; }

    /// <summary>PayPal's machine-readable issue code (e.g. REFUND_AMOUNT_EXCEEDED), if present.</summary>
    public string? Issue { get; }

    /// <summary>PayPal's debug id for support correlation, if present.</summary>
    public string? DebugId { get; }
}
