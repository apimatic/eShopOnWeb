using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment operation conflicts with the order's current state
/// (e.g. paying twice, refunding more than was captured). Maps to HTTP 409.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
