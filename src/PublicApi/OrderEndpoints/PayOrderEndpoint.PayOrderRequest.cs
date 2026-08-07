using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// How to pay an order. Supply exactly one of <see cref="Card"/> (a one-off card payment) or
/// <see cref="SavedPaymentMethodId"/> (one of the caller's saved cards).
/// </summary>
public class PayOrderRequest : BaseRequest
{
    public CardRequestModel? Card { get; set; }

    public int? SavedPaymentMethodId { get; set; }
}
