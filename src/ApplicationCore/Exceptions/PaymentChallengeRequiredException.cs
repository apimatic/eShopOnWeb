using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that would require the shopper to
/// approve the payment in a browser (for example a 3-D Secure step-up). This integration is a
/// headless, server-to-server card flow and deliberately does not build a browser approval
/// round-trip; instead it surfaces the situation clearly to the caller.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
