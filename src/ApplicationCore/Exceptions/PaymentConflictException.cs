using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the current state of the order or payment
/// (e.g. paying an already-paid order, refunding more than was captured).
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message)
        : base(message)
    {
    }
}
