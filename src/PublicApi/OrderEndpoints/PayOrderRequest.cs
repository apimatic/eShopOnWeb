using System;
using Microsoft.eShopWeb.PublicApi.Shared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    public PayOrderRequest()
    {
    }

    public PayOrderRequest(PayOrderRequest other)
    {
        Card = other.Card;
        PaymentMethodId = other.PaymentMethodId;
    }

    public int OrderId { get; set; }
    public CardPaymentRequest? Card { get; set; }
    public Guid? PaymentMethodId { get; set; }
}