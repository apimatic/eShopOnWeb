using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper to
/// approve it in a browser (e.g. a 3-D Secure step-up). This integration is headless by design and
/// does not build a browser approval round-trip, so the condition is surfaced explicitly rather than
/// silently attempting to continue.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
