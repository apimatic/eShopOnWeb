using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment action that conflicts with the order's current state (e.g. paying an already-paid order,
/// cancelling after fulfilment, or refunding more than was captured). Surfaces as HTTP 409.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
