using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment action is not valid for the order's current state (for example,
/// fulfilling an order that was never authorized, or refunding more than was captured).
/// Surfaces to the caller as a 409 Conflict with an operator-actionable message.
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message)
    {
    }
}
