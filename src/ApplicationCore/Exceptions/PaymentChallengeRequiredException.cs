namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper to
/// approve the payment in a browser (for example a 3-D Secure step-up). This integration does not
/// build a browser approval round-trip; it stops and reports the challenge so the caller knows the
/// payment could not be completed headlessly.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
