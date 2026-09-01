using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the current state of the order or payment
/// (e.g. capturing an order that was never authorized, refunding more than was captured).
/// </summary>
public class PaymentStateConflictException : Exception
{
    public PaymentStateConflictException(string message) : base(message)
    {
    }
}
