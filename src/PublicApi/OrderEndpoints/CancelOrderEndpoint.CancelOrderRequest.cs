using System;
using Microsoft.eShopWeb.PublicApi.PaymentEndpointsShared;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public CancelOrderResponse(Guid correlationId) : base(correlationId) { }
    public CancelOrderResponse() { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
