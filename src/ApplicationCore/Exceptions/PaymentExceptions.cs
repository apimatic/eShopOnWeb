using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An order was not found, or was not visible to the caller (owner-scoped).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for this account.") { }
}

/// <summary>A saved card was not found, or was not visible to the caller (owner-scoped).</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card with id {paymentMethodId} was found for this account.") { }
}

/// <summary>The request was structurally invalid (e.g. no items, no payment instrument, bad amount).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>The requested operation is not valid for the order's current state (e.g. cancel after fulfil).</summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}
