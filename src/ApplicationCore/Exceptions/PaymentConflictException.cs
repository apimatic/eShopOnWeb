using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the order's current state
/// (e.g. refunding more than was captured, cancelling a fulfilled order).
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
