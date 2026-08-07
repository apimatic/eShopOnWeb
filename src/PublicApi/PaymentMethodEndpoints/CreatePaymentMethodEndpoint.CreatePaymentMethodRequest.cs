using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    /// <summary>The card to save for the signed-in shopper. Full details are vaulted at PayPal, not stored here.</summary>
    public CardRequest? Card { get; set; }
}
