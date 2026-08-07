using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public PayOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PayOrderResponse()
    {
    }

    /// <summary>True when the order was already paid and this call was a no-op (idempotent replay).</summary>
    public bool AlreadyPaid { get; set; }

    public OrderDto Order { get; set; } = new();
}
