namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete a browser challenge (for example 3-D Secure).
/// This API does not implement an approval round-trip.
/// </summary>
public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(409, message)
    {
    }
}
