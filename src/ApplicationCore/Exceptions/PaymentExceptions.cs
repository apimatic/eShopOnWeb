using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The order (or payment method) does not exist, or does not belong to the caller.
/// Existence of other shoppers' data is deliberately not revealed.
/// </summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
    }
}

public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int paymentMethodId) : base($"Payment method {paymentMethodId} was not found.")
    {
    }
}

/// <summary>
/// The requested payment action conflicts with the current state (e.g. refunding more than
/// was captured, fulfilling an unpaid order, an authorization that can no longer be renewed).
/// The message is written to be actionable by an operator.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
