using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal returns an error for an API call. Carries PayPal's error name and debug id
/// (which should always be logged for support) and surfaces to the caller as a 502.
/// </summary>
public class PayPalApiException : Exception
{
    public PayPalApiException(string message, int statusCode, string? payPalName, string? debugId)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalName = payPalName;
        DebugId = debugId;
    }

    /// <summary>The HTTP status PayPal returned.</summary>
    public int StatusCode { get; }

    /// <summary>PayPal's machine-readable error name (e.g. UNPROCESSABLE_ENTITY).</summary>
    public string? PayPalName { get; }

    /// <summary>PayPal's debug id for the failed request — always log this.</summary>
    public string? DebugId { get; }
}
