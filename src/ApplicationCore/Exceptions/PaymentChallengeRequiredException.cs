namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve in a
/// browser (e.g. a 3-D Secure step-up). This integration is server-to-server only and does not
/// build an approval round-trip, so the operation stops and surfaces this condition instead.
/// </summary>
public class PaymentChallengeRequiredException : PaymentException
{
    public PaymentChallengeRequiredException(string message) : base(message) { }
}
