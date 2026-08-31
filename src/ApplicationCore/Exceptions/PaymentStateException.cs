using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested payment operation conflicts with the current state of the order/payment
/// (e.g. cancelling a fulfilled order, refunding more than was captured).
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
