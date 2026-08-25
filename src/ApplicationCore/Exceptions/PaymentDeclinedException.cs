using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the payment provider definitively rejects a card authorization, capture or refund
/// (e.g. the card was declined) rather than the request being malformed or the provider being unreachable.
/// </summary>
public class PaymentDeclinedException : Exception
{
    public PaymentDeclinedException(string message) : base(message)
    {
    }

    public PaymentDeclinedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
