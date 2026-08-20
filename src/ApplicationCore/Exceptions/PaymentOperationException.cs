using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PaymentOperationException : Exception
{
    public PaymentOperationException(int statusCode, string message, string? paypalDebugId = null)
        : base(message)
    {
        StatusCode = statusCode;
        PayPalDebugId = paypalDebugId;
    }

    public int StatusCode { get; }
    public string? PayPalDebugId { get; }
}
