using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is attempted against an order/payment that is not in a
/// valid state for it (e.g. capturing a payment that was never authorized, or refunding
/// more than was captured). Surfaces to the API as a 409 Conflict.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
