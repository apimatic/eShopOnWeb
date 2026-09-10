using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is not valid for the order's current money state
/// (e.g. fulfilling an order that was never authorized, or refunding beyond what was captured).
/// Maps to a 409/400 at the API boundary — it is the caller's action that is wrong, not PayPal.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
