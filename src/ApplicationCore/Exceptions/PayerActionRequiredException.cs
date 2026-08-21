using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal required a shopper to complete an approval challenge in a browser (for example 3-D Secure).
/// This integration does not implement that round-trip.
/// </summary>
public class PayerActionRequiredException : PaymentException
{
    public PayerActionRequiredException(string message)
        : base(message, HttpStatusCode.UnprocessableEntity)
    {
    }
}
