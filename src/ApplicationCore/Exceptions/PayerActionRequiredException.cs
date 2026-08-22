namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper challenge in a browser (3-D Secure / payer-action).
/// This integration does not implement that round-trip.
/// </summary>
public class PayerActionRequiredException : CheckoutException
{
    public PayerActionRequiredException(string message)
        : base(409, message)
    {
    }
}
