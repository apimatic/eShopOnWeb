using System;
using Microsoft.eShopWeb.PublicApi.PaymentModels;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId) { }
    public RefundOrderResponse() { }

    public int OrderId { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string? RefundId { get; set; }
    public OrderDto? Order { get; set; }
}
