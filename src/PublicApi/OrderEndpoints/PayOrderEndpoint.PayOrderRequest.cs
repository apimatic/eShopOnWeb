using System;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Id of one of the caller's saved cards (from POST /api/payment-methods).</summary>
    public int? PaymentMethodId { get; set; }

    /// <summary>Full card details for a one-off payment. Never stored.</summary>
    public CardDetailsRequest? Card { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
