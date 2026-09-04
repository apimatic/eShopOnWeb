namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment provider (PayPal) rejects a request. Carries the provider's
/// HTTP status so the caller can distinguish a deterministic rejection from an outage.
/// </summary>
public class PayPalApiException : ApiException
{
    public PayPalApiException(string message, int statusCode)
        : base(message, statusCode)
    {
    }
}