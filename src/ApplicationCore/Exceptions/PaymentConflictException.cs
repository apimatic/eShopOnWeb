using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment action was requested that conflicts with the payment's current state (for example refunding
/// beyond the captured amount, or capturing an order that was never authorized). Surfaced as HTTP 409.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
