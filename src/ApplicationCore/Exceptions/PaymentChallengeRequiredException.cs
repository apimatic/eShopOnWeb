namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a browser
/// (e.g. a 3-D Secure step). This integration is browser-less by design, so the attempt is surfaced
/// rather than silently building an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
