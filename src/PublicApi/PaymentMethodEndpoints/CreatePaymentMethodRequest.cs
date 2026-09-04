using Microsoft.eShopWeb.PublicApi.Shared;

namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public class CreatePaymentMethodRequest : BaseRequest
{
    public CardPaymentRequest Card { get; set; } = new CardPaymentRequest();
}