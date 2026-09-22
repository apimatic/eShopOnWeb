using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The order (payment) does not exist, or is not owned by the caller.</summary>
public class OrderPaymentNotFoundException : Exception
{
    public OrderPaymentNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for this account.")
    {
    }
}

/// <summary>A saved card does not exist, or is not owned by the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method with id {paymentMethodId} was found for this account.")
    {
    }
}

/// <summary>A payment operation was requested from a state that does not allow it, or with invalid input.</summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}
