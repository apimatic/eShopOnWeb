namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when PayPal answers a card payment with a challenge that would require the shopper
/// to approve it in a browser (e.g. 3-D Secure). This integration deliberately does not build
/// a browser approval round-trip — the condition is surfaced to the caller instead.
/// </summary>
public class PaymentChallengeRequiredException : PaymentGatewayException
{
    public PaymentChallengeRequiredException(string message)
        : base(message, statusCode: 402)
    {
    }
}
