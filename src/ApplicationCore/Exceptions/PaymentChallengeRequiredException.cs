namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (order status PAYER_ACTION_REQUIRED / a "payer-action" HATEOAS link). This
/// integration deliberately does NOT build a browser approval round-trip; it surfaces the condition
/// so a human can act on it.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
