using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The authorization behind an order has gone stale and PayPal can no longer renew it;
/// the shopper must pay again before the order can be fulfilled.
/// </summary>
public class PaymentAuthorizationRenewalException : Exception
{
    public PaymentAuthorizationRenewalException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
