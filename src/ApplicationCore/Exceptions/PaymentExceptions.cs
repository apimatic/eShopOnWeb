using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>An order was not found, or does not belong to the caller (not distinguished, to avoid leaking existence).</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId) : base($"Order {orderId} was not found.") { }
}

/// <summary>A saved card was not found, or does not belong to the caller.</summary>
public class SavedPaymentMethodNotFoundException : Exception
{
    public SavedPaymentMethodNotFoundException(int id) : base($"Saved payment method {id} was not found.") { }
}

/// <summary>A payment operation was invalid given the current state (a caller-fixable 4xx).</summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}
