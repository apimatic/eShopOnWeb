using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal rejects or fails a request. Carries PayPal's <c>debug_id</c> (when present)
/// so the failure can be traced with PayPal support. Surfaces as a 502 Bad Gateway.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? debugId = null, string? payPalName = null)
        : base(message)
    {
        DebugId = debugId;
        PayPalName = payPalName;
    }

    /// <summary>PayPal's <c>debug_id</c> from the error response, if any.</summary>
    public string? DebugId { get; }

    /// <summary>PayPal's machine-readable error <c>name</c>, if any (e.g. INSTRUMENT_DECLINED).</summary>
    public string? PayPalName { get; }
}
