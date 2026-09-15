namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when PayPal answers a card payment with a contingency that requires the shopper
/// to approve the transaction in a browser (e.g. a 3-D Secure challenge). This integration
/// is intentionally browserless, so this surfaces as an explicit, reported gap rather than
/// an approval round-trip being built.
/// </summary>
public class PayPalChallengeRequiredException : PaymentException
{
    public PayPalChallengeRequiredException(string message) : base(message)
    {
    }
}
