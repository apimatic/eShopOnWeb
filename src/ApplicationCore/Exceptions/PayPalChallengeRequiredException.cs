namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a challenge that requires the shopper to approve
/// in a browser (3-D Secure / PAYER_ACTION_REQUIRED). This integration is server-to-server only and
/// deliberately does NOT build a browser approval round-trip; it surfaces the condition instead.
/// </summary>
public class PayPalChallengeRequiredException : PaymentException
{
    public PayPalChallengeRequiredException(string message) : base(message) { }
}
