using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed at the payment processor. Carries the processor's error
/// name and a message safe to surface to API callers.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, string? gatewayErrorName = null, int? httpStatusCode = null)
        : base(message)
    {
        GatewayErrorName = gatewayErrorName;
        HttpStatusCode = httpStatusCode;
    }

    public string? GatewayErrorName { get; }
    public int? HttpStatusCode { get; }
}

/// <summary>
/// The processor asked for a shopper interaction (e.g. a 3D Secure challenge) that this
/// headless integration does not support.
/// </summary>
public class PayerActionRequiredException : PaymentGatewayException
{
    public PayerActionRequiredException(string message, string? gatewayErrorName = null)
        : base(message, gatewayErrorName)
    {
    }
}
