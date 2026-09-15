using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal returns an error response. Carries PayPal's own error name and debug id so an
/// operator can trace the request with PayPal support. Never carries card data.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? payPalErrorName, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's machine-readable error name (e.g. UNPROCESSABLE_ENTITY, INSTRUMENT_DECLINED).</summary>
    public string? PayPalErrorName { get; }

    /// <summary>PayPal's correlation/debug id from the response, for support tracing.</summary>
    public string? DebugId { get; }
}
