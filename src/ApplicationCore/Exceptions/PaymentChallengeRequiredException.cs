using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper to approve in
/// a browser (e.g. a 3-D Secure step). This integration does not build a browser approval round-trip, so
/// the operation stops and reports it.
/// </summary>
public class PaymentChallengeRequiredException : Exception
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
