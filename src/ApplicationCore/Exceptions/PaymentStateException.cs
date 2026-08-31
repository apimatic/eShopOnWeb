using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment operation conflicts with the current state of the order or payment.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
