using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation was rejected for a domain reason the caller can act on — e.g. paying an order that is
/// not awaiting payment, refunding more than was captured, or using a card that is not the caller's. Surfaces
/// as an HTTP 4xx with the (caller-safe) message.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
