using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateOrderResponse()
    {
    }

    /// <summary>Identifier of the newly created order (top-level, so callers can drive the pay flow).</summary>
    public int OrderId { get; set; }

    public string PaymentStatus { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = "USD";
}
