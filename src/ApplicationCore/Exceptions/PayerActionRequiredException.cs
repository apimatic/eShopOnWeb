namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// PayPal answered a card payment with a challenge that needs a shopper to approve in a browser
/// (PAYER_ACTION_REQUIRED). This integration is browser-free by design, so this is a STOP-and-report
/// condition rather than something the API can complete on its own. Surfaced as HTTP 422.
/// </summary>
public class PayerActionRequiredException : PayPalProviderException
{
    public PayerActionRequiredException(string message) : base(message, statusCode: 422)
    {
    }
}
