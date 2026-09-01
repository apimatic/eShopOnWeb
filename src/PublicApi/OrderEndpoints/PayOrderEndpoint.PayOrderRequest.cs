using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public int OrderId { get; set; }

    /// <summary>Raw card details for a one-off payment. Mutually exclusive with PaymentMethodId.</summary>
    public CardDetailsRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards (POST /api/payment-methods) to pay with.</summary>
    public int? PaymentMethodId { get; set; }
}
