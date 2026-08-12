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

    /// <summary>The identifier of the newly placed order (top-level, so callers can drive flows).</summary>
    public int OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }
}
