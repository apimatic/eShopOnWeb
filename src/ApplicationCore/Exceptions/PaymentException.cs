using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the payment processor rejects or fails a payment operation.
/// The message is safe to surface to API callers.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when a payment operation is not valid for the current state of the order
/// (e.g. paying an already-paid order, refunding more than was captured).
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}

public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}

public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId) : base($"Payment method {paymentMethodId} was not found.") { }
}
