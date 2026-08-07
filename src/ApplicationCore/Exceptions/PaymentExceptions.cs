using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>Thrown when a requested order does not exist or does not belong to the caller.</summary>
public class OrderNotFoundException : Exception
{
    public OrderNotFoundException(int orderId)
        : base($"No order with id {orderId} was found for the current user.") { }
}

/// <summary>Thrown when a requested saved card does not exist or does not belong to the caller.</summary>
public class PaymentMethodNotFoundException : Exception
{
    public PaymentMethodNotFoundException(int paymentMethodId)
        : base($"No saved payment method with id {paymentMethodId} was found for the current user.") { }
}

/// <summary>Thrown when a request is structurally invalid (bad card, no line items, etc.).</summary>
public class PaymentInputException : Exception
{
    public PaymentInputException(string message) : base(message) { }
}

/// <summary>Thrown when a payment operation is not valid for the order's current state.</summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}

/// <summary>
/// Thrown when PayPal returns an error (declined card, validation failure, etc.). Carries a
/// caller-safe message and the PayPal debug id (if any) for correlation. Never contains card data.
/// </summary>
public class PayPalApiException : Exception
{
    public int? PayPalStatusCode { get; }
    public string? DebugId { get; }

    public PayPalApiException(string message, int? payPalStatusCode = null, string? debugId = null, Exception? inner = null)
        : base(message, inner)
    {
        PayPalStatusCode = payPalStatusCode;
        DebugId = debugId;
    }
}
