using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An order was not found, or is not owned by the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}.") { }
}

/// <summary>A saved card was not found, or is not owned by the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}.") { }
}

/// <summary>An operation was attempted against an order whose state does not allow it.</summary>
public class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message) : base(message) { }
}
