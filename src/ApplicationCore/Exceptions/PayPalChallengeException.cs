using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper
/// to approve it in a browser (order status <c>PAYER_ACTION_REQUIRED</c> / a payer-action link).
/// This integration deliberately does not build a browser approval round-trip; instead the
/// condition is surfaced to the caller as an actionable error.
/// </summary>
public class PayPalChallengeException : PayPalPaymentException
{
    public PayPalChallengeException(string message) : base(message)
    {
    }
}
