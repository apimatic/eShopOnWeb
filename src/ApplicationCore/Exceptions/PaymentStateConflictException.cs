using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the current state of the order or payment
/// (e.g. fulfilling an unpaid order, refunding beyond the captured amount).
/// </summary>
public class PaymentStateConflictException : Exception
{
    public PaymentStateConflictException(string message) : base(message)
    {
    }
}
