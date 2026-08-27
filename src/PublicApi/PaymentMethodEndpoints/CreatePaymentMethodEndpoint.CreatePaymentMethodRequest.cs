using Microsoft.eShopWeb.PublicApi.OrderEndpoints;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>
    /// Card details to vault with PayPal. Never persisted or logged by this app.
    /// </summary>
    public CardRequest Card { get; set; } = new();
}
