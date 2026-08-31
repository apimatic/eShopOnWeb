using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusChangeResponse : BaseResponse
{
    public OrderStatusChangeResponse(Guid correlationId) : base(correlationId) {}
    public OrderStatusChangeResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
