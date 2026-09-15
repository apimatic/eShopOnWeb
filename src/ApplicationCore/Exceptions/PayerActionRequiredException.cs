namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete a browser challenge (for example 3-D Secure).
/// This integration does not implement an approval round-trip.
/// </summary>
public class PayerActionRequiredException : PaymentException
{
    public string? PayPalDebugId { get; }
    public string? PayPalOrderId { get; }

    public PayerActionRequiredException(string? paypalOrderId, string? debugId)
        : base(
            "PayPal required a shopper to approve this card payment in a browser (payer-action / 3-D Secure challenge). " +
            "This integration does not implement a browser approval round-trip.",
            409)
    {
        PayPalOrderId = paypalOrderId;
        PayPalDebugId = debugId;
    }
}
