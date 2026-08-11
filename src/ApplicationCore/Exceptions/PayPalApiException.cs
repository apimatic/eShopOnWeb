using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a PayPal REST call fails. Carries the HTTP status code and PayPal's own machine-readable
/// issue name (when present) so callers can react to specific conditions such as an expired authorization.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(int statusCode, string? issue, string message, string? rawBody = null)
        : base(message)
    {
        StatusCode = statusCode;
        Issue = issue;
        RawBody = rawBody;
    }

    public int StatusCode { get; }

    /// <summary>PayPal's machine-readable issue name, e.g. "AUTHORIZATION_EXPIRED", "INSTRUMENT_DECLINED".</summary>
    public string? Issue { get; }

    public string? RawBody { get; }
}
