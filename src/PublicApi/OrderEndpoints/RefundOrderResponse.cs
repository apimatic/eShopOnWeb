using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public RefundOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public RefundOrderResponse()
    {
    }

    public int OrderId { get; set; }
    public Guid RefundId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? RefundStatus { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
}