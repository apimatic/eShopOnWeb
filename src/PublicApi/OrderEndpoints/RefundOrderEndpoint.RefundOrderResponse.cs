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

    /// <summary>True when the order was already refunded and this call was a no-op (idempotent replay).</summary>
    public bool AlreadyRefunded { get; set; }

    public OrderDto Order { get; set; } = new();
}
