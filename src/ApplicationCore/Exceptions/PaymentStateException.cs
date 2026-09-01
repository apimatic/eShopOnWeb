using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment action conflicts with the order's current state (e.g. capturing an
/// order that was never authorized, refunding more than was captured). Maps to HTTP 409.
/// The message is operator-facing and must say what can be done instead.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
