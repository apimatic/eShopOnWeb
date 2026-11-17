using Microsoft.eShopWeb.ApplicationCore.Payments;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderRequest
{
    public int OrderId { get; set; }
    public int? PaymentMethodId { get; set; }
    public CardRequest? Card { get; set; }

    public PayOrderInput ToInput()
    {
        return new PayOrderInput
        {
            PaymentMethodId = PaymentMethodId,
            Card = Card?.ToCardInput()
        };
    }
}
