using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raised when the payment gateway (PayPal) rejects a request or is unreachable. Carries the safe,
/// non-sensitive parts of PayPal's error model (name / message / debug_id) plus the HTTP status so a
/// caller can be given a meaningful response and support can correlate via <see cref="DebugId"/>.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(
        string message,
        int? httpStatusCode = null,
        string? errorName = null,
        string? debugId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        ErrorName = errorName;
        DebugId = debugId;
    }

    /// <summary>HTTP status code returned by PayPal, when the failure came from an API response.</summary>
    public int? HttpStatusCode { get; }

    /// <summary>PayPal's machine-readable error name (e.g. <c>UNPROCESSABLE_ENTITY</c>, <c>INSTRUMENT_DECLINED</c>).</summary>
    public string? ErrorName { get; }

    /// <summary>PayPal's <c>debug_id</c> for support correlation.</summary>
    public string? DebugId { get; }
}
