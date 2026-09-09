namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that requires the shopper to approve it in a
/// browser (e.g. 3-D Secure). This integration is deliberately browser-free, so we surface the
/// condition rather than building an approval round-trip.
/// </summary>
public class PaymentChallengeException : PaymentException
{
    public PaymentChallengeException(string message)
        : base(message, statusCode: 402)
    {
    }
}
