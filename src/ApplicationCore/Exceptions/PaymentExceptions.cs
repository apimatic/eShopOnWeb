using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>The requested order/payment/saved-card does not exist for this caller (maps to 404).</summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>
/// The operation is not valid for the payment's current state — e.g. fulfilling an unpaid order,
/// cancelling a fulfilled one, or refunding more than was captured (maps to 409/400).
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}
