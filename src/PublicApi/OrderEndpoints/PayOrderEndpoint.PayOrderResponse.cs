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

    public int OrderId { get; set; }

    /// <summary>Payment state after this request: typically <c>Paid</c>.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public OrderDto Order { get; set; } = new();

    /// <summary>Optional human-readable note (e.g. that the order was already paid).</summary>
    public string? Message { get; set; }
}
