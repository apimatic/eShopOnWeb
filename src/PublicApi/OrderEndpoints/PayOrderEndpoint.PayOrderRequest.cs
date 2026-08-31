using System;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>One-off card details for this payment. Ignored when PaymentMethodId is set.</summary>
    public CardDetailsDto? Card { get; set; }

    /// <summary>Id of one of the caller's saved cards (from POST api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto Payment { get; set; } = new();
}
