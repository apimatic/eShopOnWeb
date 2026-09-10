using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot proceed given the order's current state (for example paying
/// an order that is not awaiting payment, or refunding more than was captured). Surfaced as HTTP 409.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message) { }
}
