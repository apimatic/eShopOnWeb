namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete a browser challenge (for example 3-D Secure).
/// This integration does not implement an approval round-trip.
/// </summary>
public class PaymentChallengeRequiredException : CheckoutException
{
    public PaymentChallengeRequiredException(string? paypalDebugId = null)
        : base(
            "PayPal required a shopper approval step in the browser (for example 3-D Secure). " +
            "This integration does not collect that approval." +
            (string.IsNullOrEmpty(paypalDebugId) ? string.Empty : $" PayPal debug id: {paypalDebugId}."),
            409)
    {
        PayPalDebugId = paypalDebugId;
    }

    public string? PayPalDebugId { get; }
}
