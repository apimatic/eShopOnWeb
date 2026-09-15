using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment/fulfilment operation is attempted from an invalid order or payment state
/// (for example fulfilling an order that was never authorized, or over-refunding a capture).
/// Surfaced to the caller as a 409/422 with an operator-actionable message.
/// </summary>
public class OrderPaymentException : Exception
{
    public OrderPaymentException(string message) : base(message)
    {
    }

    public OrderPaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
