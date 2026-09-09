using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order does not exist, or does not belong to the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order found with id {orderId}.") { }
}

/// <summary>The requested saved card does not exist, or does not belong to the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved card found with id {paymentMethodId}.") { }
}

/// <summary>The request could not name a valid payment source (neither card details nor a saved card).</summary>
public class InvalidPaymentSourceException : Exception
{
    public InvalidPaymentSourceException(string message) : base(message) { }
}
