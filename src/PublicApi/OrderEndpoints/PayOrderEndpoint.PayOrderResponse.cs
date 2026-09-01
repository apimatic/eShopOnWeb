using System;
using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId) { }
    public PayOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
