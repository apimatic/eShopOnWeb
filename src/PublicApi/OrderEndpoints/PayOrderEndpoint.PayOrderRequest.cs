using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>A saved card to pay with. Either this or Card, never both.</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>One-off card details. Either this or PaymentMethodId, never both.</summary>
    public CardDto? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentStateDto? Payment { get; set; }
}

