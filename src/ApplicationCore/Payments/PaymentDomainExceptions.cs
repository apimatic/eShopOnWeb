using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The requested payment/order/saved-card was not found for this caller.</summary>
public sealed class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}

/// <summary>The requested operation is not valid for the payment's current state
/// (e.g. capturing an order that was never authorized).</summary>
public sealed class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}
