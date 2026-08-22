namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete a browser challenge (for example 3-D Secure).
/// This integration is headless and does not implement an approval round-trip.
/// </summary>
public class PayPalPayerActionRequiredException : CheckoutException
{
    public PayPalPayerActionRequiredException(string message, string? payerActionUrl = null)
        : base(409, message)
    {
        PayerActionUrl = payerActionUrl;
    }

    public string? PayerActionUrl { get; }
}
