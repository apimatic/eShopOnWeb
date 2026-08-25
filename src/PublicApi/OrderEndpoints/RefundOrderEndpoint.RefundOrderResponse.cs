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

    public int RefundId { get; set; }
    public string PayPalRefundId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public decimal Amount { get; set; }
    public int OrderId { get; set; }
}
