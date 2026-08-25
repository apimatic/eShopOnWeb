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

    public string RefundId { get; set; } = "";
    public int OrderId { get; set; }
    public string Status { get; set; } = "";
    public decimal Amount { get; set; }
    public OrderDto Order { get; set; } = null!;
}
