using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal rejects or fails a request. The <see cref="Message"/> is safe to surface to
/// an operator and, where PayPal supplied one, carries the issuer/processor reason so the operator
/// can act on it. <see cref="DebugId"/> is PayPal's correlation id for support.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        DebugId = debugId;
    }

    public string? DebugId { get; }
}
