using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The payment processor rejected or failed an operation. Carries the processor's
/// error name/issue so operators can act on it.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? gatewayErrorName = null, string? gatewayDebugId = null)
        : base(message)
    {
        GatewayErrorName = gatewayErrorName;
        GatewayDebugId = gatewayDebugId;
    }

    public string? GatewayErrorName { get; }
    public string? GatewayDebugId { get; }
}
