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

    /// <summary>The order's payment state after this call (e.g. "Refunded").</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? RefundId { get; set; }

    /// <summary>True when the order was already refunded and no new refund was issued (idempotent replay).</summary>
    public bool AlreadyRefunded { get; set; }
}
