using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal rejects or fails a request. Carries the PayPal error name and debug id so an
/// operator can act on it. Surfaced to the API as a 502 Bad Gateway.
/// </summary>
public class PayPalGatewayException : Exception
{
    public PayPalGatewayException(string message, string? payPalErrorName = null, string? debugId = null)
        : base(message)
    {
        PayPalErrorName = payPalErrorName;
        DebugId = debugId;
    }

    /// <summary>The machine-readable PayPal error name (for example, INSTRUMENT_DECLINED), if any.</summary>
    public string? PayPalErrorName { get; }

    /// <summary>The PayPal debug id for correlation with PayPal support/logs, if any.</summary>
    public string? DebugId { get; }
}
