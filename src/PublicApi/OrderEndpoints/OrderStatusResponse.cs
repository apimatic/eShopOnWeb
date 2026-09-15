using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusResponse : BaseResponse
{
    public OrderStatusResponse(Guid correlationId) : base(correlationId) { }

    public OrderStatusResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
