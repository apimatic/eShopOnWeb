using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The order does not exist, or does not belong to the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.")
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

/// <summary>The request conflicts with the order's current payment state (or with a concurrent request).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}

/// <summary>The request is malformed; the message says what to fix.</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
