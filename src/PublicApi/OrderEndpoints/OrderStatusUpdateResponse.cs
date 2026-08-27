using System;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class OrderStatusUpdateResponse : BaseResponse
{
    public OrderStatusUpdateResponse(Guid correlationId) : base(correlationId) {}
    public OrderStatusUpdateResponse() {}

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
