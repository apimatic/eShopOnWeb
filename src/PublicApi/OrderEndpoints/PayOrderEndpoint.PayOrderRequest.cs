using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Payment instruction for an order. Provide exactly one of <see cref="Card"/> (a one-off payment)
/// or <see cref="SavedPaymentMethodId"/> (one of the shopper's saved cards).
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public CardRequest? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
