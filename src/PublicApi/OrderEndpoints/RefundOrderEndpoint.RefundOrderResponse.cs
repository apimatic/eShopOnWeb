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

    /// <summary>Payment state after this request: <c>Refunded</c>.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public OrderDto Order { get; set; } = new();

    public string? Message { get; set; }
}
