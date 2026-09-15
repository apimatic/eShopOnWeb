namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card operation with a challenge that requires the shopper to approve in a
/// browser (3-D Secure / PAYER_ACTION_REQUIRED). This integration deliberately does not build an
/// approval round-trip — it surfaces the gap so an operator/shopper knows a browser step is needed.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message)
    {
    }
}
